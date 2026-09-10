# smartdoor-adapter-c-sharp

## Description

This project provides a C# implementation of a plugin adapter for Axini's standalone SmartDoor application (SUT). It connects the Axini Modeling Platform (AMP) to the standalone SmartDoor SUT. This implementation serves as a reference for Axini's Adapter Training.
For more information see the [Axini documentation](https://course02.axini.com/docs/tech/adapters/index.html).

**Current Version:** v0.91 (work in progress)

The architecture of this adapter is based on [Axini's Java adapter](https://github.com/Axini/smartdoor-adapter-java), which follows the [adapter specification](https://course02.axini.com/docs/tech/adapters/index.html).

For more information on the current implementation see the [class diagram](./SmartdoorAdapter/Doc/class-diagram.md) or [sequence diagram](./SmartdoorAdapter/Doc/dotnet-adapter-sequence.md).

The software is distributed under the MIT license, see LICENSE.txt.

## Building the Application

To build the application using Visual Studio with .NET 8.0:
1. Download and install the free community version of Visual Studio from [Microsoft](https://visualstudio.microsoft.com/vs/community/).
2. The project uses NuGet for dependency management and should build out of the box.

## Running the Application

In addition to the C# implementation, you will need:
- Access to Axini's Modelling Platform (AMP). Contact Axini for [more information](https://www.axini.com/nl/contact/).
- The standalone SUT project, which is a Java project available from [Axini's GitHub](https://github.com/Axini/standalone-smartdoor-java).

### Steps to Run:


#### Running the .NET Adapter
The adapter requires three variables to run (regardless of OS):
  - **name**: The adapter name as it will appear in AMP.
  - **url**: The AMP endpoint (a websocket address, e.g., `wss://some.endpoint.axini.com:123/adapters`).
  - **token**: An API token obtained from the AMP.

These variables are passed to the adapter as CLI parameters:
`SmartdoorAdapter <name> <url> <apikey>`

##### Running on Windows
  - Using standard windows functionality, run the `.exe` with parameters, e.g:
  ```shell
  SmartdoorAdapter.exe <name> <url> <token>
  ```

  - Using WSL
    1. Optionally allow for mirrored mode in WSL.
        - Create a .wslconfig in the user's home directory (C:\Users\$(USER_NAME))
        - Add:
        ```shell
        [wsl2]
        networkingMode=mirrored
         ```

    2. [Install dotnet on WSL](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu?source=post_page-----515e8160bae6--------------------------------#supported-distributions). The current adapter is tested with .NET 8.0 during the sudo apt install <package-name> step. .NET 9.x has NOT been tested.

    3. Follow Running on Linux instructions

##### Running on Linux (Ubuntu 24.04)
  - Install dotnet-sdk. The package can be installed via apt with the following command: `sudo apt install dotnet-sdk-8.0`. .NET 9.x has NOT been tested.
  - Change directory to this repo e.g: `cd smartdoor-adapter-c-sharp`.
  - Restore the dependencies: `dotnet restore`
  - Build the project: `dotnet build`
  - Run the project: `dotnet run --project ./SmartdoorAdapter <name> <url> <token>`

  - Optionally one can run the test suite with `dotnet test`
## Current Limitations

- Limited unit and integration tests are available.
- There are various issues which are not clear yet. These are marked with TODO throughout the code.
