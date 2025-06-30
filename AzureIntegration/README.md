# AzureForHumans

A human-friendly wrapper for Azure SDK that simplifies common Azure operations like deploying Azure Functions, managing resource groups, and working with storage accounts.

## Installation

```bash
dotnet add package AzureForHumans
```

## Features

- **Simple Azure Function Deployment**: Deploy Azure Functions with minimal configuration
- **Resource Group Management**: Create and manage Azure resource groups easily
- **Storage Account Integration**: Simplified storage account creation and management
- **App Service Plan Management**: Easy creation of app service plans for your functions
- **Built-in Authentication**: Uses Azure DefaultCredential for seamless authentication

## Quick Start

### Basic Usage

```csharp
using AzureIntegration;

// Initialize Azure client
var azureCloud = new AzureCloud();

// Create a resource group
var resourceGroup = await azureCloud.CreateResourceGroup("my-resource-group");

// Deploy an Azure Function
var azureFunction = await azureCloud.DeployAzureFunction(
    "/path/to/your/function/project", 
    "my-function-app"
);

Console.WriteLine($"Function deployed at: https://{azureFunction.Url}");
```

### Working with Resource Groups

```csharp
var azureCloud = new AzureCloud();

// Get all resource groups
var resourceGroups = await azureCloud.GetResourceGroups();
foreach (var rg in resourceGroups)
{
    Console.WriteLine($"Resource Group: {rg.Name}");
}

// Create a new resource group
var newRg = await azureCloud.CreateResourceGroup("my-new-rg");

// Delete a resource group
await azureCloud.DeleteResourceGroup("my-old-rg");
```

### Storage Account Management

```csharp
var azureCloud = new AzureCloud();
var resourceGroup = await azureCloud.CreateResourceGroup("my-rg");

// Create a storage account
var storageAccount = await resourceGroup.CreateStorageAccount("myapp");
Console.WriteLine($"Storage Account: {storageAccount.Name}");
Console.WriteLine($"Connection String: {storageAccount.ConnectionString}");
```

## Prerequisites

- .NET 9.0 or later
- Azure subscription
- Azure credentials configured (Azure CLI, Visual Studio, environment variables, etc.)

## Authentication

This library uses Azure's `DefaultAzureCredential` which automatically tries various authentication methods:

1. Environment variables
2. Managed Identity
3. Visual Studio authentication
4. Azure CLI authentication
5. Interactive browser authentication

Make sure you're authenticated using one of these methods before using the library.

## Configuration

The library uses the following default settings:

- **Location**: West Europe (you can modify this in the source code)
- **Runtime**: .NET 9.0 isolated
- **App Service Plan**: Dynamic (Consumption) plan

## Example Projects

Check out the sample Azure Function project included in this repository to see how to structure your functions for deployment.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the LGPL-2.1 License.
