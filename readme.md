
# Smartdoor Adapter.NET v0.9

## Description

This project provides a C# implementation of a plugin adapter for Axini's standalone SmartDoor application (SUT). It connects the Axini Modeling Platform (AMP) to the standalone SmartDoor SUT. This implementation serves as a reference for Axini's Adapter Training.
For more information see the [Axini documentation](https://course02.axini.com/docs/tech/adapters/index.html).

**Current Version:** v0.91 (work in progress)

The architecture of this adapter is based on [Axini's Java adapter](https://github.com/Axini/smartdoor-adapter-java), which follows the [adapter specification](https://course02.axini.com/docs/tech/adapters/index.html).

For more information on the current implementation see the [class diagram](./SmartdoorAdapter/Doc/class-diagram.md) or [sequence diagram](./SmartdoorAdapter/Doc/dotnet-adapter-sequence.md).

This software is distributed under the MIT license. See `LICENSE.txt` for details.

## Building the Application

To build the application using Visual Studio with .NET 9.0:
1. Download and install the free community version of Visual Studio from [Microsoft](https://visualstudio.microsoft.com/vs/community/).
2. The project uses NuGet for dependency management and should build out of the box.

## Running the Application

In addition to the C# implementation, you will need:
- Access to Axini's Modelling Platform (AMP). Contact Axini for [more information](https://www.axini.com/nl/contact/).
- The standalone SUT project, which is a Java project available from [Axini's GitHub](https://github.com/Axini/standalone-smartdoor-java).

### Steps to Run:


1. Build the standalone SUT:
   ```shell
   java -jar target/standalone-smartdoor-0.1-jar-with-dependencies.jar
   ```
   (Refer to the SUT documentation for specifics.)

2. Start the .NET Adapter on Windows:
   - **Option 1:** Run the full command line:
     ```shell
     SmartdoorAdapter.exe <name> <url> <token>
     ```
     - **name**: The adapter name as it will appear in AMP.
     - **url**: The AMP endpoint (a websocket address, e.g., `wss://some.endpoint.axini.com:123/adapters`).
     - **token**: A simple auth token obtained from the AMP/adapters site.

   - **Option 2:** Define the environment variables `AXINI_ADAPTER_NAME`, `AXINI_AUTH_TOKEN` and `AXINI_AMP_HOST`, 
     then run:
     ```shell
     SmartdoorAdapter.exe
     ```

 3. Run the .NET Adapter on wsl (this should work under Ubuntu 24.*):
 
    - Optionally allow for mirrored mode in WSL. 
        - Create a .wslconfig in the user's home directory (C:\Users\$(USER_NAME))
        - Add 
        ```shell 
        [wsl2]
        networkingMode=mirrored
         ```
    
    - [Install dotnet on WSL, link](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu?source=post_page-----515e8160bae6--------------------------------#supported-distributions)
      The current adapter is tested with .NET 8.0 during the `sudo apt install <package-name>` step. .NET 9.x has NOT been tested.
        
    - Clone the project: git clone https://github.com/pointlesspun/SmartdoorAdapter.git
    
    - Go to the solution directory. 
    
      - Restore the dependencies: `dotnet restore`
      - Build the project: `dotnet build`
      - Set the environment variables in ~/.bashrc
      - Run the project: `dotnet run --project ./SmartdoorAdapter`
      - Optionally one can run the test suite with `dotnet test`

## Current Limitations

- Limited unit and integration tests are available.
- There are various issues which are not clear yet. These are marked with the initials ML throughout the code.