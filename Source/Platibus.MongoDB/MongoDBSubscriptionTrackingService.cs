﻿// The MIT License (MIT)
// 
// Copyright (c) 2017 Jesse Sweetland
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
using MongoDB.Driver;
using Platibus.Diagnostics;
#if NET452 || NET461
using System.Configuration;
#endif
#if NETSTANDARD2_0
using Platibus.Config;
#endif

namespace Platibus.MongoDB
{
    /// <inheritdoc />
    /// <summary>
    /// An implementation of <see cref="T:Platibus.ISubscriptionTrackingService" /> that stores subscriptions
    /// in a MongoDB database
    /// </summary>
    public class MongoDBSubscriptionTrackingService : ISubscriptionTrackingService
    {
        /// <summary>
        /// The default name of the collection that will be used to store subscription information
        /// </summary>
        public const string DefaultCollectionName = "platibus.subscriptions";

        private readonly IDiagnosticService _diagnosticService;
        private readonly IMongoCollection<SubscriptionDocument> _subscriptions;

        /// <summary>
        /// Initializes a new <see cref="MongoDBSubscriptionTrackingService"/>
        /// </summary>
        /// <param name="options">Options governing the behavior of the service</param>
        public MongoDBSubscriptionTrackingService(MongoDBSubscriptionTrackingOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var myCollectionName = string.IsNullOrWhiteSpace(options.CollectionName)
                ? DefaultCollectionName
                : options.CollectionName;

            _diagnosticService = options.DiagnosticService ?? DiagnosticService.DefaultInstance;
            _subscriptions = options.Database.GetCollection<SubscriptionDocument>(myCollectionName);
        }

        /// <inheritdoc />
        /// <summary>
        /// Initializes a new <see cref="T:Platibus.MongoDB.MongoDBSubscriptionTrackingService" /> with the specified
        /// <paramref name="connectionStringSettings" /> and <paramref name="databaseName" />
        /// </summary>
        /// <param name="connectionStringSettings">The connection string to use to connect to the
        /// MongoDB database</param>
        /// <param name="databaseName">(Optional) The name of the database to use.  If omitted,
        /// the default database identified in the <paramref name="connectionStringSettings" />
        /// will be used</param>
        /// <param name="collectionName">(Optional) The name of the collection in which 
        /// subscription documents will be stored.  If omitted, the
        /// <see cref="F:Platibus.MongoDB.MongoDBSubscriptionTrackingService.DefaultCollectionName" /> will be used</param>
        [Obsolete]
        public MongoDBSubscriptionTrackingService(ConnectionStringSettings connectionStringSettings, string databaseName = null, string collectionName = DefaultCollectionName)
        : this(new MongoDBSubscriptionTrackingOptions(MongoDBHelper.Connect(connectionStringSettings, databaseName))
        {
            CollectionName = collectionName
        })
        {
        }

        /// <inheritdoc />
        public Task AddSubscription(TopicName topic, Uri subscriber, TimeSpan ttl = new TimeSpan(),
            CancellationToken cancellationToken = new CancellationToken())
        {
            if (topic == null) throw new ArgumentNullException(nameof(topic));
            if (subscriber == null) throw new ArgumentNullException(nameof(subscriber));

            try
            {
                var expires = ttl <= TimeSpan.Zero
                    ? DateTime.MaxValue
                    : DateTime.UtcNow.Add(ttl);

                var fb = Builders<SubscriptionDocument>.Filter;
                var filter = fb.Eq(s => s.Topic, topic.ToString()) &
                             fb.Eq(s => s.Subscriber, subscriber.ToString());

                var update = Builders<SubscriptionDocument>.Update
                    .Set(s => s.Expires, expires);

                return _subscriptions.UpdateOneAsync(filter, update, new UpdateOptions {IsUpsert = true}, cancellationToken);
            }
            catch (Exception ex)
            {
                _diagnosticService.Emit(new MongoDBEventBuilder(this, MongoDBEventType.MongoDBUpdateFailed)
                {
                    Detail = $"Error uperting subscription to topic {topic} for subscriber {subscriber}",
                    CollectionName = _subscriptions.CollectionNamespace.CollectionName,
                    DatabaseName = _subscriptions.Database.DatabaseNamespace.DatabaseName,
                    Exception = ex,
                    Topic = topic
                }.Build());

                throw;
            }
        }

        /// <inheritdoc />
        public Task RemoveSubscription(TopicName topic, Uri subscriber, CancellationToken cancellationToken = new CancellationToken())
        {
            if (topic == null) throw new ArgumentNullException(nameof(topic));
            if (subscriber == null) throw new ArgumentNullException(nameof(subscriber));

            try
            {
                var fb = Builders<SubscriptionDocument>.Filter;
                var filter = fb.Eq(s => s.Topic, topic.ToString()) &
                             fb.Eq(s => s.Subscriber, subscriber.ToString());

                return _subscriptions.DeleteManyAsync(filter, cancellationToken);
            }
            catch (Exception ex)
            {
                _diagnosticService.Emit(new MongoDBEventBuilder(this, MongoDBEventType.MongoDBDeleteFailed)
                {
                    Detail = $"Error deleting subscription(s) to topic {topic} for subscriber {subscriber}",
                    CollectionName = _subscriptions.CollectionNamespace.CollectionName,
                    DatabaseName = _subscriptions.Database.DatabaseNamespace.DatabaseName,
                    Exception = ex,
                    Topic = topic
                }.Build());

                throw;
            }
        }

        /// <inheritdoc />
        public async Task<IEnumerable<Uri>> GetSubscribers(TopicName topic, CancellationToken cancellationToken = new CancellationToken())
        {
            try
            {
                var fb = Builders<SubscriptionDocument>.Filter;
                var filter = fb.Eq(s => s.Topic, topic.ToString()) &
                             fb.Gt(s => s.Expires, DateTime.UtcNow);                

                var subscrptionDocuments = await _subscriptions.Find(filter).ToListAsync(cancellationToken);
                return subscrptionDocuments.Select(s => new Uri(s.Subscriber));
            }
            catch (Exception ex)
            {
                _diagnosticService.Emit(new MongoDBEventBuilder(this, MongoDBEventType.MongoDBFindFailed)
                {
                    Detail = $"Error finding subscription(s) to topic {topic}",
                    CollectionName = _subscriptions.CollectionNamespace.CollectionName,
                    DatabaseName = _subscriptions.Database.DatabaseNamespace.DatabaseName,
                    Exception = ex,
                    Topic = topic
                }.Build());

                throw;
            }
        }
    }
}
