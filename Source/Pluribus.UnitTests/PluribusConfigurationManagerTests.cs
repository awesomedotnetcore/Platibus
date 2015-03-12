﻿using NUnit.Framework;
using Pluribus.Config;
using System.Linq;

namespace Pluribus.UnitTests
{
    public class PluribusConfigurationManagerTests
    {
        public class TestConfigurationHook : IConfigurationHook
        {
            public void Configure(PluribusConfiguration configuration)
            {
                configuration.AddHandlingRule(".*(?i)example.*", new MessageHandlerStub());
            }
        }

        [Test]
        public void Given_Configuration_Hook_When_Loading_Configuration_Then_Message_Route_Added()
        {
            var configuration = PluribusConfigurationManager.LoadConfiguration();

            var messageRoutes = configuration.HandlingRules.ToList();
            Assert.That(messageRoutes.Count, Is.EqualTo(1));
            Assert.That(messageRoutes[0].MessageSpecification, Is.InstanceOf<MessageNamePatternSpecification>());

            var namePatternSpec = (MessageNamePatternSpecification)messageRoutes[0].MessageSpecification;
            Assert.That(namePatternSpec.NameRegex.ToString(), Is.EqualTo(".*(?i)example.*"));

            Assert.That(messageRoutes[0].MessageHandler, Is.InstanceOf<MessageHandlerStub>());
        }
    }
}
