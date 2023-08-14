using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Management.Compute;
using Microsoft.Azure.Management.Compute.Models;
using Microsoft.Azure.Management.Network.Models;
using Microsoft.Rest;
using System.Net.Http.Json;

namespace VmssTroubleshootingSandbox
{
    public class VmssStartTroubleShooter
    {
        private static AccessToken s_token;

        public static async Task<bool> StartAsync(
            string subscriptionId,
            string resourceGroupName,
            string vmssInstanceId,
            string agentExtensionName)
        {
            // Get Azure Credentials
            Console.Write("Attempting to get Azure credentials");

            s_token =
                new DefaultAzureCredential().GetToken(
                    new TokenRequestContext(new[] { "https://management.azure.com/.default" }));
            var defaultTokenCredential = new TokenCredentials(s_token.Token);

            // Create Compute Client
            using var computeClient = 
                new ComputeManagementClient(defaultTokenCredential) { SubscriptionId = subscriptionId };

            // Start the VMSS VM Instance
            await StartVmssVmInstanceAsync(computeClient, resourceGroupName, vmssInstanceId);

            // Get the VMSS VM Extension Status and check for error. If it is failed, attempt to update the Extension
            var agentExtension = await GetExtensionStatusAsync(computeClient, resourceGroupName, vmssInstanceId, agentExtensionName); 
            if(agentExtension?.ProvisioningState != "Succeeded")
            {
                // The VMSS Extension is failed, try and update it as a trouble shooting step.
                Console.WriteLine($"{vmssInstanceId} Since extension is failed, beginning to update it as a potential fix");

                var updateExtension = await CreateOrUpdateExtensionAsync(
                    computeClient,
                    resourceGroupName,
                    vmssInstanceId,
                    agentExtension,
                    "Update");

                if (updateExtension?.Status != "Succeeded")
                {
                    // The update Extension failed, will try to delete it and then reinstall it
                    var deleteExtension = await DeleteExtensionAsync(
                        computeClient,
                        resourceGroupName,
                        vmssInstanceId,
                        agentExtension);

                    if(deleteExtension?.Status == "Succeeded")
                    {
                        // The delete Extension succeeded, try to reinstall it
                        var createExtension = await CreateOrUpdateExtensionAsync(
                            computeClient,
                            resourceGroupName,
                            vmssInstanceId,
                            agentExtension,
                            "Create");

                        if (createExtension?.Status != "Succeeded")
                        {
                            Console.WriteLine($"The agent was successfully uninstalled and reinstalled, check the VM for proper functionality");
                        }
                        else 
                        {
                            // The Agent was unable to be installed, this will require escalation
                            throw new Exception("Unable to reinstall the extension, the issue will require escalation");
                        }
                    }
                    else
                    {
                        // The Agent was unable to be deleted, this will require escalation
                        throw new Exception("Unable to delete the extension, this will need to be esclated");
                    }
                }
                else
                {
                    // The update Extension succeeded
                    Console.WriteLine($"Updating extension succeeded, try starting the VMSS VM again");
                }
            }
            else
            {
                Console.WriteLine($"No issue detected with agent extension, terminating");
            }

            return true;
        }

        private static async Task<AzureAsyncOperationResult?> PollOperationAsync(string pollingForName, string? pollingOperationUri, AccessToken accessToken)
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken.Token);
            var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, pollingOperationUri));
            var operationResult = await response.Content.ReadFromJsonAsync<AzureAsyncOperationResult>();
            while (operationResult?.Status == "InProgress")
            {
                int pollingIncrementInSeconds = 30;
                Console.WriteLine($"Current status of polling for '{pollingForName}' is '{operationResult?.Status}', trying again in {pollingIncrementInSeconds} seconds");
                await Task.Delay(pollingIncrementInSeconds * 1000);
                response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, pollingOperationUri));
                operationResult = await response.Content.ReadFromJsonAsync<AzureAsyncOperationResult>();
            }

            return operationResult;
        }

        private static string? AzurePollingOperationUri(HttpResponseMessage httpResponseMessage)
        {
            return httpResponseMessage.Headers.GetValues("Azure-AsyncOperation").FirstOrDefault();
        }

        private static Exception OperationException(string operationName, string? message = null)
        {
            var messageText = message != null ?  $". Message: {message}" : string.Empty;
            return new Exception($"{operationName} failed operation{message}");
        }

        private static async Task<AzureAsyncOperationResult?> StartVmssVmInstanceAsync(
            ComputeManagementClient computeClient,
            string resourceGroupName,
            string vmInstanceName)
        {
            Console.WriteLine($"{vmInstanceName} Beginning start VM");

            var beginStartOperation = 
                await computeClient.VirtualMachines.BeginStartWithHttpMessagesAsync(resourceGroupName, vmInstanceName);

            if (!beginStartOperation.Response.IsSuccessStatusCode)
            {
                throw OperationException(nameof(beginStartOperation));
            }

            Console.WriteLine($"{vmInstanceName} Start VM request accepted by Azure");

            var startOperation = await PollOperationAsync("Start VM", AzurePollingOperationUri(beginStartOperation.Response), s_token);

            if (startOperation?.Status != "Succeeded")
            {
                throw OperationException(nameof(startOperation), startOperation?.Error?.Message);
            }

            var vmStartErrorCode = string.IsNullOrEmpty(startOperation?.Error?.Message) ? "None" : startOperation?.Error?.Code;
            var vmStartErrorMessage = string.IsNullOrEmpty(startOperation?.Error?.Message) ? "None" : startOperation?.Error?.Message;

            Console.WriteLine($"{vmInstanceName} Start VM operation completed");
            Console.WriteLine($"{vmInstanceName} Start status: {startOperation?.Status}");
            Console.WriteLine($"{vmInstanceName} Start recorded error code: {vmStartErrorCode}");
            Console.WriteLine($"{vmInstanceName} Start recorded error message: {vmStartErrorMessage}");

            return startOperation;
        }

        private static async Task<VirtualMachineExtension?> GetExtensionStatusAsync(
            ComputeManagementClient computeClient,
            string resourceGroupName,
            string vmInstanceName,
            string agentExtensionName)
        {
            Console.WriteLine($"{vmInstanceName} Getting '{agentExtensionName}' extension status");

            var agentExtension =
                await computeClient.VirtualMachineExtensions.GetWithHttpMessagesAsync(resourceGroupName, vmInstanceName, agentExtensionName);

            if(!agentExtension.Response.IsSuccessStatusCode)
            {
                throw OperationException(nameof(agentExtension));
            }

            Console.WriteLine($"{vmInstanceName} extension provisioning status for {agentExtensionName}: {agentExtension?.Body?.ProvisioningState}");

            return agentExtension?.Body;
        }

        private static async Task<AzureAsyncOperationResult?> CreateOrUpdateExtensionAsync(
            ComputeManagementClient computeClient,
            string resourceGroupName,
            string vmInstanceName,
            VirtualMachineExtension? virtualMachineExtension,
            string kind)
        {

            var beginCreateOrUpdateOperation =
                await computeClient.VirtualMachineExtensions.BeginCreateOrUpdateWithHttpMessagesAsync(
                    resourceGroupName,
                    vmInstanceName,
                    virtualMachineExtension?.Name,
                    virtualMachineExtension);

            if (!beginCreateOrUpdateOperation.Response.IsSuccessStatusCode)
            {
                throw OperationException(nameof(beginCreateOrUpdateOperation));
            }

            Console.WriteLine($"{vmInstanceName} {kind} extension request accepted by Azure");

            var createOrUpdateExtensionOperation = await PollOperationAsync(
                $"VM extension {kind}",
                AzurePollingOperationUri(beginCreateOrUpdateOperation.Response),
                s_token);

            Console.WriteLine($"{vmInstanceName} {kind} extension operation completed");

            var extensionCreateOrUpdateErrorCode = 
                string.IsNullOrEmpty(createOrUpdateExtensionOperation?.Error?.Message) ?
                "None" :
                createOrUpdateExtensionOperation?.Error?.Code;

            var extensionCreateOrUpdateErrorMessage = 
                string.IsNullOrEmpty(createOrUpdateExtensionOperation?.Error?.Message) ?
                "None" :
                createOrUpdateExtensionOperation?.Error?.Message;

            Console.WriteLine($"{vmInstanceName} {kind} extension status: {createOrUpdateExtensionOperation?.Status}");
            Console.WriteLine($"{vmInstanceName} {kind} extension recorded error code: {extensionCreateOrUpdateErrorCode}");
            Console.WriteLine($"{vmInstanceName} {kind} extension recorded error message: {extensionCreateOrUpdateErrorMessage}");

            return createOrUpdateExtensionOperation;
        }

        private static async Task<AzureAsyncOperationResult?> DeleteExtensionAsync(
            ComputeManagementClient computeClient,
            string resourceGroupName,
            string vmInstanceName,
            VirtualMachineExtension? virtualMachineExtension)
        {
            Console.WriteLine($"{vmInstanceName} Will attempt to delete the extension");

            var beginDeleteOperation =
                await computeClient.VirtualMachineExtensions.BeginDeleteWithHttpMessagesAsync(
                    resourceGroupName,
                    vmInstanceName,
                    virtualMachineExtension?.Name);

            if (!beginDeleteOperation.Response.IsSuccessStatusCode)
            {
                throw OperationException(nameof(beginDeleteOperation));
            }

            Console.WriteLine($"{vmInstanceName} Delete extension request accepted by Azure");

            var deleteExtensionOperation = await PollOperationAsync(
                "VM Extension Delete",
                AzurePollingOperationUri(beginDeleteOperation.Response),
                s_token);

            Console.WriteLine($"{vmInstanceName} Update extension operation completed");

            var extensionDeleteErrorCode =
                string.IsNullOrEmpty(deleteExtensionOperation?.Error?.Message) ?
                "None" :
                deleteExtensionOperation?.Error?.Code;

            var extensionDeleteErrorMessage =
                string.IsNullOrEmpty(deleteExtensionOperation?.Error?.Message) ?
                "None" :
                deleteExtensionOperation?.Error?.Message;

            Console.WriteLine($"{vmInstanceName} Delete extension status: {deleteExtensionOperation?.Status}");
            Console.WriteLine($"{vmInstanceName} Delete extension recorded error code: {extensionDeleteErrorCode}");
            Console.WriteLine($"{vmInstanceName} Delete extension recorded error message: {extensionDeleteErrorMessage}");

            return deleteExtensionOperation;
        }
    }
}
