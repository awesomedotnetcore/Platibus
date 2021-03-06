﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Platibus.Diagnostics;
using Xunit;

namespace Platibus.UnitTests.Diagnostics
{
    [Trait("Category", "UnitTests")]
    [Trait("Dependency", "InfluxDB")]
    public class InfluxDBSinkTests
    {
        protected TimeSpan SampleRate = TimeSpan.FromSeconds(5);

        // docker run -it --rm --name influxdb -e INFLUXDB_ADMIN_ENABLED=true -p 8096:8086 -p 8093:8083 influxdb
        protected InfluxDBOptions Options = new InfluxDBOptions(new Uri("http://localhost:8096"), "platibus");
        protected List<DiagnosticEvent> DiagnosticEvents = new List<DiagnosticEvent>();

        public InfluxDBSinkTests()
        {
            CreateDatabase();
        }

        private void CreateDatabase()
        {
            var uri = new UriBuilder(Options.Uri)
            {
                Path = "query",
                Query = "q=CREATE DATABASE " + Options.Database
            }.Uri;

            using (var client = new HttpClient())
            {
                var response = client.PostAsync(uri, new StringContent("")).Result;
                Assert.True(response.IsSuccessStatusCode, $"Error creating InfluxDB database '{Options.Database}': {response}");
            }
        }

        [Fact]
        public async Task AcknowledgementFailuresAreRecorded()
        {
            GivenQueuedMessageFlowWithAcknowledgementFailure();
            await WhenConsumingEvents();
            
        }

        protected void GivenQueuedMessageFlowWithAcknowledgementFailure()
        {
            var fakes = new DiagnosticFakes(this);
            DiagnosticEvents.AddRange(fakes.QueuedMessageFlowWithAcknowledgementFailure());
        }

        protected async Task WhenConsumingEvents()
        {
            var sink = new InfluxDBSink(Options, SampleRate);
            var consumeTasks = DiagnosticEvents.Select(async e => await sink.ConsumeAsync(e)).ToList();
            await Task.WhenAll(consumeTasks);
            sink.RecordMeasurements();
        }
    }
}
