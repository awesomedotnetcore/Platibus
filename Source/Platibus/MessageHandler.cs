﻿// The MIT License (MIT)
// 
// Copyright (c) 2016 Jesse Sweetland
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Platibus.Diagnostics;
using Platibus.Serialization;

namespace Platibus
{
    internal class MessageHandler 
    {
        private readonly IDiagnosticService _diagnosticService;
        private readonly MessageMarshaller _messageMarshaller;

        public MessageHandler(MessageMarshaller messageMarshaller, IDiagnosticService diagnosticService = null)
        {
            _diagnosticService = diagnosticService ?? DiagnosticService.DefaultInstance;
            _messageMarshaller = messageMarshaller ?? throw new ArgumentNullException(nameof(messageMarshaller));
        }

        public async Task HandleMessage(IEnumerable<IMessageHandler> messageHandlers, Message message,
            IMessageContext messageContext, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (message.Headers.Expires < DateTime.UtcNow)
            {
                await _diagnosticService.EmitAsync(
                    new DiagnosticEventBuilder(this, DiagnosticEventType.MessageExpired)
                    {
                        Detail = "Discarding message that expired " + message.Headers.Expires,
                        Message = message
                    }.Build(), cancellationToken);
                
                messageContext.Acknowledge();
                return;
            }

            var messageContent = _messageMarshaller.Unmarshal(message);

            var handlingTasks = messageHandlers.Select(handler =>
                handler.HandleMessage(messageContent, messageContext, cancellationToken));

            await Task.WhenAll(handlingTasks);
        }
    }
}