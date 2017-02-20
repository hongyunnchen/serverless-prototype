﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Serverless.Worker.Extensions;
using Serverless.Worker.Managers;
using Serverless.Worker.Models;
using Serverless.Worker.Providers;
using Docker.DotNet;
using Docker.DotNet.Models;
using Newtonsoft.Json.Linq;

namespace Serverless.Worker.Entities
{
    public class Container
    {
        private static HttpClient HttpClient { get; set; }

        private static DockerClient DockerClient { get; set; }

        public string Id { get; private set; }

        public DateTime LastExecutionTime { get; set; }

        private Uri Uri { get; set; }

        static Container()
        {
            Container.HttpClient = new HttpClient();

            var dockerUri = new Uri(ConfigurationProvider.DockerUri);
            Container.DockerClient = new DockerClientConfiguration(endpoint: dockerUri).CreateClient();
        }

        public Container(string id, string ipAddress)
        {
            this.Id = id;
            this.LastExecutionTime = DateTime.UtcNow;
            this.Uri = new Uri(string.Format(
                format: ConfigurationProvider.ContainerUriTemplate,
                arg0: ipAddress));
        }

        public async Task<ExecutionResponse> Execute(ExecutionRequest request, CancellationToken cancellationToken)
        {
            var response = await HttpClient
                .PostAsync(
                    requestUri: this.Uri,
                    content: request.Input,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            var output = await response.Content
                .FromJson<JToken>()
                .ConfigureAwait(continueOnCapturedContext: false);

            return new ExecutionResponse
            {
                Output = output
            };
        }

        public Task Delete()
        {
            return Container.DockerClient.Containers
                .RemoveContainerAsync(
                    id: this.Id,
                    parameters: new ContainerRemoveParameters
                    {
                        Force = true
                    });
        }

        public static async Task<Container> Create(string deploymentId, int memorySize, CancellationToken cancellationToken)
        {
            var dockerUri = new Uri(ConfigurationProvider.DockerUri);
            var dockerClient = new DockerClientConfiguration(endpoint: dockerUri).CreateClient();

            var parameters = new CreateContainerParameters
            {
                Image = "serverless-node",
                HostConfig = new HostConfig
                {
                    MemorySwap = memorySize,
                    CPUShares = memorySize,
                    Binds = new List<string>
                    {
                        "C:/deployments/" + deploymentId + ":C:/function:ro"
                    }
                },
                Volumes = new Dictionary<string, object>
                {
                    { "C:/function", new { } }
                }
            };

            var createResponse = await dockerClient.Containers
                .CreateContainerAsync(parameters: parameters)
                .ConfigureAwait(continueOnCapturedContext: false);

            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            await Container.DockerClient.Containers
                .StartContainerAsync(
                    id: createResponse.ID,
                    parameters: new ContainerStartParameters { })
                .ConfigureAwait(continueOnCapturedContext: false);

            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            var inspectResponse = await Container.DockerClient.Containers
                .InspectContainerAsync(id: createResponse.ID)
                .ConfigureAwait(continueOnCapturedContext: false);

            return new Container(
                id: inspectResponse.ID,
                ipAddress: inspectResponse.NetworkSettings.Networks[ConfigurationProvider.DockerNetworkName].IPAddress);
        }
    }
}