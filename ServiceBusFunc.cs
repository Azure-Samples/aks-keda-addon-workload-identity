using System;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ServiceBusProj
{
    public class ServiceBusFunc
    {
        private readonly ILogger<ServiceBusFunc> _logger;

        public ServiceBusFunc(ILogger<ServiceBusFunc> logger)
        {
            _logger = logger;
        }

        [Function(nameof(ServiceBusFunc))]
        public void Run([ServiceBusTrigger("upper-case", Connection = "AzureWebJobsServiceBus")] ServiceBusReceivedMessage message)
        {
            _logger.LogInformation("Message ID: {id}", message.MessageId);
            _logger.LogInformation("C# ServiceBus queue trigger function processed message: {body}", message.Body);
            _logger.LogInformation("Message Content-Type: {contentType}", message.ContentType);
        }
    }
}
