# Axini .Net Adapter Class Diagram

The following is describing the implementation of the Axini .Net Adapter.

The AdapterCore orchestrates overall state of the program and the message interchange between an IBroker and IHandler.

The IBroker and its implementation, the BrokerConnection, represent the state and connection to the Axini Modelling Platform (AMP).

The IHandler and its implementation, the SmartDoorHandler, represent the state and connection to the System Under Test (SUT). In this
example a fictional Smart Door system.

The ManagedClientWebSocket is fit-for-purpose wrapper around a [System.Net.WebSockets.ClientWebSocket](https://learn.microsoft.com/en-us/dotnet/api/system.net.websockets.clientwebsocket?view=net-8.0).
It simplifies the interface via an event-driven approach, handles the state management, handles exception handling and offers properties required to work with the AMP and Sut.

For more information about the sequence of interactions see the [associated sequence diagram](./dotnet-adapter-sequence.md).
For more information on the overall Adapter design, see the [Axini documentation](https://course02.axini.com/docs/tech/adapters/index.html).
For information on how to build and run the system see the [readme](../../readme.md).

```mermaid
---
title: Axini .Net Adapter
---
classDiagram
    
    class IDisposable
    <<interface>> IDisposable

    class AdapterCore

    class IBroker
    <<interface>> IBroker

    class BrokerConnection

    class IHandler
    <<interface>> IHandler

    class ManagedClientWebSocket


    IDisposable <|-- AdapterCore

    AdapterCore o-- IBroker
    AdapterCore o-- IHandler

    IDisposable <|-- BrokerConnection
    IBroker <|-- BrokerConnection

    IDisposable <|-- SmartdoorHandler
    IHandler <|-- SmartdoorHandler

    BrokerConnection *-- ManagedClientWebSocket
    SmartdoorHandler *-- ManagedClientWebSocket

    class AdapterCore{
        +AdapterCoreState: State

        +Start() Task
        +Start(CancellationToken) Task
        +Stop()
    }

    class IBroker{
        +ManagedSocketState: State

        +OnClosed(object, (WebSocketCloseStatus?, string))
        +OnMessage(object, Message)

        +Connect() bool
        +Listen() Task
        +SendAnnounceMessage(string, List~Label~,Configuration) Task
        +SendErrorMessage(string) Task
        +SendReadyMessage(string) Task
        +SendResponseMessage(string, string, ByteString) Task
        +SendStimulus(Label, ByteString, ulong) Task
        +Close(WebSocketCloseStatus, string)
    }

    class IHandler{
        +MaxConnectionAttempts: int
        +Channel: string
        +SupportedLabels: List~Label~
        +Configuration: Configuration

        +OnResponse(object, string)
        +OnError(object, Exception)
        
        +Start() Task~bool~
        +Stop()
        +Reset() Task
        +Stimulate() Task
        +ToPhysicalLabel(string): Task~ByteString~
    }

    class ManagedClientWebSocket{
        +apiKey: string
        +Name: string
        +ConnectionUri: Uri
        +State: ManagedSocketState
        +SocketState: WebSocketState
        +ConnectionTimeout: int
        +MaxConnectionAttempts: int

        +OnBinaryReceived(object,ArraySegment~byte~)
        +OnTextReceived(object,string)
        +OnClosed(object,(WebSocketStatus?, string))

        +Connect(|int|): Task~ManagedClientWebSocket~
        +SendText(string, |int|) Task
        +SendBinary(string, |int|) Task
        +Receive(byte[]) Task~WebSocketReceiveResult~
        +Listen() Task
        +Close(|WebSocketCloseStatus|, |string|, |int|)
    }
```