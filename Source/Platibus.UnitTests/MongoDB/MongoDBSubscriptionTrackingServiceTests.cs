﻿using Platibus.MongoDB;
using Xunit;

namespace Platibus.UnitTests.MongoDB
{
    [Trait("Category", "UnitTests")]
    [Trait("Dependency", "MongoDB")]
    [Collection(MongoDBCollection.Name)]
    public class MongoDBSubscriptionTrackingServiceTests : SubscriptionTrackingServiceTests<MongoDBSubscriptionTrackingService>
    {
        public MongoDBSubscriptionTrackingServiceTests(MonogDBFixture fixture) 
            : base(fixture.SubscriptionTrackingService)
        {
        }
    }
}
