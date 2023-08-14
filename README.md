This is a basic VMSS Flex troubleshooter sandbox. Currently, it will try to reinstall a failed Agent Extension. It should do the following items:
1. Start the VMSS VM
1. Check the status of the provided agent extension		
1. If the agent extension is a failed state, it will attempt to reinstall the extension by calling an update
1. If the update fails, it will attempt to delete the extension
1. Once the extension is deleted, it will attempt to reinstall the extension

This is a sandbox idea for testing troubleshooting VMSS starts based on Agent Extension failures, but it could be configured with a similar logic to handle other issues with start.

This project depends on the [.NET App Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-7.0&tabs=windows). It should be implemented with the following schema:

```json
{
  "SubscriptionId": {subscription id},
  "ResourceGroupName": {resource group name},
  "VmssInstanceId": {vmss instance id (should only be the name part, e.g . vmssname_8digitGuid},
  "AgentExtensionName": {agent name in namespace format}
}
```
