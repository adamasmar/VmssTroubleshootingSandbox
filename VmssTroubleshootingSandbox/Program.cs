using Microsoft.Extensions.Configuration;
using VmssTroubleshootingSandbox;

Console.WriteLine("Building configuration");

var configuration = new ConfigurationBuilder().AddUserSecrets<Program>().Build();

Console.WriteLine("Getting configuration values");

var subscriptionId = configuration["SubscriptionId"];
var resourceGroupName = configuration["ResourceGroupName"];
var vmssInstanceId = configuration["VmssInstanceId"];
var agentExtensionName = configuration["AgentExtensionName"];

Console.WriteLine("Configuration settings for this session");
Console.WriteLine($"SubscriptionId: {subscriptionId}");
Console.WriteLine($"ResourceGroupName: {resourceGroupName}");
Console.WriteLine($"VmssInstanceId: {vmssInstanceId}");
Console.WriteLine($"AgentExtensionName: {agentExtensionName}");

Console.WriteLine("Starting troubleshooter");

Task.Run(async () =>
{
    await VmssStartTroubleShooter.StartAsync(subscriptionId, resourceGroupName, vmssInstanceId, agentExtensionName);
}).Wait();

Console.WriteLine("Completed troubleshooter");