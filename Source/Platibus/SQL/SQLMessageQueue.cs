﻿using Common.Logging;
using Platibus.Security;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Transactions;

namespace Platibus.SQL
{
    class SQLMessageQueue : IDisposable
    {
        private static readonly ILog Log = LogManager.GetLogger(LoggingCategories.SQL);

        private readonly ConnectionStringSettings _connectionStringSettings;
        private readonly ISQLDialect _dialect;
        private readonly QueueName _queueName;
        private readonly IQueueListener _listener;
        
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly SemaphoreSlim _concurrentMessageProcessingSlot;
        private readonly bool _autoAcknowledge;
        private readonly int _maxAttempts;
        private readonly BufferBlock<SQLQueuedMessage> _queuedMessages;
        private readonly TimeSpan _retryDelay;

        private bool _disposed;
        private int _initialized;

        public SQLMessageQueue(ConnectionStringSettings connectionStringSettings, ISQLDialect dialect, QueueName queueName, IQueueListener listener, QueueOptions options = default(QueueOptions))
        {
            if (connectionStringSettings == null) throw new ArgumentNullException("connectionStringSettings");
            if (dialect == null) throw new ArgumentNullException("dialect");
            if (queueName == null) throw new ArgumentNullException("queueName");
            if (listener == null) throw new ArgumentNullException("listener");
            
            _connectionStringSettings = connectionStringSettings;
            _dialect = dialect;
            _queueName = queueName;

            _listener = listener;
            _autoAcknowledge = options.AutoAcknowledge;
            _maxAttempts = options.MaxAttempts <= 0 ? 10 : options.MaxAttempts;
            _retryDelay = options.RetryDelay < TimeSpan.Zero ? TimeSpan.Zero : options.RetryDelay;

            var concurrencyLimit = options.ConcurrencyLimit <= 0
                ? QueueOptions.DefaultConcurrencyLimit
                : options.ConcurrencyLimit;
            _concurrentMessageProcessingSlot = new SemaphoreSlim(concurrencyLimit);

            _cancellationTokenSource = new CancellationTokenSource();
            _queuedMessages = new BufferBlock<SQLQueuedMessage>(new DataflowBlockOptions
            {
                CancellationToken = _cancellationTokenSource.Token
            });
        }

        public async Task Init()
        {
            if (Interlocked.Exchange(ref _initialized, 1) == 0)
            {
                await EnqueueExistingMessages();

                // ReSharper disable once UnusedVariable
                var processingTask = ProcessQueuedMessages(_cancellationTokenSource.Token);
            }
        }

        public async Task Enqueue(Message message, IPrincipal senderPrincipal)
        {
            CheckDisposed();

            // SQL calls are not async to avoid the need for TransactionAsyncFlowOption
            // and dependency on .NET 4.5.1 and later
            var queuedMessage = InsertQueuedMessage(message, senderPrincipal);

            await _queuedMessages.SendAsync(queuedMessage).ConfigureAwait(false);
            // TODO: handle accepted == false
        }

        // ReSharper disable once UnusedMethodReturnValue.Local
        private async Task ProcessQueuedMessages(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var nextQueuedMessage = await _queuedMessages.ReceiveAsync(cancellationToken).ConfigureAwait(false);

                // We don't want to wait on this task; we want to allow concurrent processing
                // of messages.  The semaphore will be released by the ProcessQueuedMessage
                // method.

                // ReSharper disable once UnusedVariable
                var messageProcessingTask = ProcessQueuedMessage(nextQueuedMessage, cancellationToken);
            }
        }

        // ReSharper disable once UnusedMethodReturnValue.Local
        private async Task ProcessQueuedMessage(SQLQueuedMessage queuedMessage, CancellationToken cancellationToken)
        {
            var messageId = queuedMessage.Message.Headers.MessageId;
            var attemptCount = 0;
            var abandoned = false;
            while (!abandoned)
            {
                attemptCount++;

                Log.DebugFormat("Processing queued message {0} (attempt {1} of {2})...",
                    messageId, attemptCount, _maxAttempts);

                var context = new SQLQueuedMessageContext(queuedMessage);
                cancellationToken.ThrowIfCancellationRequested();

                await _concurrentMessageProcessingSlot.WaitAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var message = queuedMessage.Message;
                    await _listener.MessageReceived(message, context, cancellationToken).ConfigureAwait(false);
                    if (_autoAcknowledge && !context.Acknowledged)
                    {
                        await context.Acknowledge().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Log.WarnFormat("Unhandled exception handling queued message {0}", ex, messageId);
                }
                finally
                {
                    _concurrentMessageProcessingSlot.Release();
                }

                if (context.Acknowledged)
                {
                    Log.DebugFormat("Message acknowledged.  Marking message {0} as acknowledged...", messageId);
                    // TODO: Implement journaling
                    UpdateQueuedMessage(queuedMessage, DateTime.UtcNow, null);
                    Log.DebugFormat("Message {0} acknowledged successfully", messageId);
                    return;
                }
                else if (attemptCount >= _maxAttempts)
                {
                    Log.WarnFormat("Maximum attempts to proces message {0} exceeded", messageId);
                    abandoned = true;
                }

                if (abandoned)
                {
                    UpdateQueuedMessage(queuedMessage, null, DateTime.UtcNow);
                    return;
                }

                Log.DebugFormat("Message not acknowledged.  Retrying in {0}...", _retryDelay);
                await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        private SQLQueuedMessage InsertQueuedMessage(Message message, IPrincipal senderPrincipal)
        {
            using (var connection = _connectionStringSettings.OpenConnection())
            using (var scope = new TransactionScope(TransactionScopeOption.Required))
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandType = CommandType.Text;
                    command.CommandText = _dialect.InsertQueuedMessageCommand;

                    var headers = message.Headers;

                    command.SetParameter(_dialect.MessageIdParameterName, (Guid)headers.MessageId); 
                    command.SetParameter(_dialect.QueueNameParameterName, (string)_queueName); 
                    command.SetParameter(_dialect.MessageNameParameterName, (string)headers.MessageName);  
                    command.SetParameter(_dialect.OriginationParameterName, headers.Origination == null ? null : headers.Origination.ToString()); 
                    command.SetParameter(_dialect.DestinationParameterName, headers.Destination == null ? null : headers.Destination.ToString()); 
                    command.SetParameter(_dialect.ReplyToParameterName, headers.ReplyTo == null ? null : headers.ReplyTo.ToString());
                    command.SetParameter(_dialect.ExpiresParameterName, headers.Expires);
                    command.SetParameter(_dialect.ContentTypeParameterName, headers.ContentType);
                    command.SetParameter(_dialect.SenderPrincipalParameterName, SerializePrincipal(senderPrincipal));
                    command.SetParameter(_dialect.HeadersParameterName, SerializeHeaders(headers)); 
                    command.SetParameter(_dialect.MessageContentParameterName, message.Content);

                    command.ExecuteNonQuery();
                }
                scope.Complete();
            }
            return new SQLQueuedMessage(message, senderPrincipal);
        }

        private IEnumerable<SQLQueuedMessage> SelectQueuedMessages()
        {
            var queuedMessages = new List<SQLQueuedMessage>();

            using (var connection = _connectionStringSettings.OpenConnection())
            using (var scope = new TransactionScope(TransactionScopeOption.Required))
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandType = CommandType.Text;
                    command.CommandText = _dialect.SelectQueuedMessagesCommand;
                    command.SetParameter(_dialect.QueueNameParameterName, (string)_queueName); 
                    command.SetParameter(_dialect.CurrentDateParameterName, DateTime.UtcNow);

                    using (var reader = command.ExecuteReader())
                    {
                        while(reader.Read())
                        {
                            var messageContent = reader.GetString("MessageContent");
                            var headers = DeserializeHeaders(reader.GetString("Headers"));
                            var senderPrincipal = DeserializePrincipal(reader.GetString("SenderPrincipal"));
                            var message = new Message(headers, messageContent);
                            var queuedMessage = new SQLQueuedMessage(message, senderPrincipal);
                            queuedMessages.Add(queuedMessage);
                        }
                    }
                }
                scope.Complete();
            }
            
            return queuedMessages;
        }

        private void UpdateQueuedMessage(SQLQueuedMessage queuedMessage, DateTime? acknowledged, DateTime? abandoned)
        {
            using (var connection = _connectionStringSettings.OpenConnection())
            using (var scope = new TransactionScope(TransactionScopeOption.Required))
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandType = CommandType.Text;
                    command.CommandText = _dialect.UpdateQueuedMessageCommand;
                    command.SetParameter(_dialect.MessageIdParameterName, (Guid)queuedMessage.Message.Headers.MessageId);
                    command.SetParameter(_dialect.AcknowledgedParameterName, acknowledged);
                    command.SetParameter(_dialect.AbandonedParameterName, abandoned);
                    
                    command.ExecuteNonQuery();
                }
                scope.Complete();
            }
        }

        private async Task EnqueueExistingMessages()
        {
            var queuedMessages = SelectQueuedMessages();
            foreach (var queuedMessage in queuedMessages)
            {
                Log.DebugFormat("Enqueueing existing message ID {0}...", queuedMessage.Message.Headers.MessageId);
                await _queuedMessages.SendAsync(queuedMessage).ConfigureAwait(false);
            }
        }

        private string SerializePrincipal(IPrincipal principal)
        {
            if (principal == null) return null;

            var senderPrincipal = new SenderPrincipal(principal);
            using (var memoryStream = new MemoryStream())
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize(memoryStream, senderPrincipal);
                var base64String = Convert.ToBase64String(memoryStream.GetBuffer());
                return base64String;
            }
        }

        private string SerializeHeaders(IMessageHeaders headers)
        {
            if (headers == null) return null;

            using (var writer = new StringWriter())
            {
                foreach (var header in headers)
                {
                    var headerName = header.Key;
                    var headerValue = header.Value;
                    writer.Write(string.Format("{0}: ", headerName));
                    using (var headerValueReader = new StringReader(headerValue))
                    {
                        var multilineContinuation = false;
                        string line;
                        while ((line = headerValueReader.ReadLine()) != null)
                        {
                            if (multilineContinuation)
                            {
                                // Prefix continuation with whitespace so that subsequent
                                // lines are not confused with different headers.
                                line = "    " + line;
                            }
                            writer.WriteLine(line);
                            multilineContinuation = true;
                        }
                    }
                }
                return writer.ToString();
            }
        }

        private IPrincipal DeserializePrincipal(string base64String)
        {
            if (string.IsNullOrWhiteSpace(base64String)) return null;

            var bytes = Convert.FromBase64String(base64String);
            using (var memoryStream = new MemoryStream(bytes))
            {
                var formatter = new BinaryFormatter();
                return (IPrincipal)formatter.Deserialize(memoryStream);
            }
        }

        private IMessageHeaders DeserializeHeaders(string headerString)
        {
            var headers = new MessageHeaders();
            if (string.IsNullOrWhiteSpace(headerString)) return headers;
            
            var currentHeaderName = (HeaderName)null;
            var currentHeaderValue = new StringWriter();
            var finishedReadingHeaders = false;
            var lineNumber = 0;
            
            string currentLine;
            using (var reader = new StringReader(headerString))
            {
                while (!finishedReadingHeaders && (currentLine = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    if (string.IsNullOrWhiteSpace(currentLine))
                    {
                        if (currentHeaderName != null)
                        {
                            headers[currentHeaderName] = currentHeaderValue.ToString();
                            currentHeaderName = null;
                            currentHeaderValue = new StringWriter();
                        }

                        finishedReadingHeaders = true;
                        continue;
                    }

                    if (currentLine.StartsWith(" ") && currentHeaderName != null)
                    {
                        // Continuation of previous header value
                        currentHeaderValue.WriteLine();
                        currentHeaderValue.Write(currentLine.Trim());
                        continue;
                    }

                    // New header.  Finish up with the header we were just working on.
                    if (currentHeaderName != null)
                    {
                        headers[currentHeaderName] = currentHeaderValue.ToString();
                        currentHeaderValue = new StringWriter();
                    }

                    if (currentLine.StartsWith("#"))
                    {
                        // Special line. Ignore.
                        continue;
                    }

                    var separatorPos = currentLine.IndexOf(':');
                    if (separatorPos < 0)
                    {
                        throw new FormatException(string.Format("Invalid header on line {0}:  Character ':' expected",
                            lineNumber));
                    }

                    if (separatorPos == 0)
                    {
                        throw new FormatException(
                            string.Format(
                                "Invalid header on line {0}:  Character ':' found at position 0 (missing header name)",
                                lineNumber));
                    }

                    currentHeaderName = currentLine.Substring(0, separatorPos);
                    currentHeaderValue.Write(currentLine.Substring(separatorPos + 1).Trim());
                }
            }

            return headers;
        }

        protected virtual void CheckDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(GetType().FullName);
        }

        ~SQLMessageQueue()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            Dispose(true);
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            _cancellationTokenSource.Cancel();
            if (disposing)
            {
                _concurrentMessageProcessingSlot.Dispose();
                _cancellationTokenSource.Dispose();
            }
        }
    }
}