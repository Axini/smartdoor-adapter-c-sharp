
Todo
----

_Version 0.91_

* Await further comments of the team.


Done
----

_Version 0.91_

* Upgrade to .NET 9

_Version 0.9_

* Add/Updated sequence - and class diagram
* Verify all todo remarks have been addressed
* Increase code documentation
* Increase test coverage, specifically add unit tests for the core
* Create sequence diagrams of the .net version
* Move Kestrel to its own namespace matching the original.
* Clean up code (esp. the managedsocket), review first.
* Verify if the output in AMP is the same in the java adapter and .net adapter.
* Verify the adapter's behavior is the same as the Java adapter. Eg the java adapter may stay up after the AMP closes the connection whereas
  the current implementation just terminates.
	* After completed test go back into announced
	* If SUT is not available go back into announced
	* If AMP is not available try to connect until it becomes available or user cancels
* [BUG] In case the SUT is not up yet and the AMP fails the test. The Handler will null on a clientsocket in the connect loop() because the close will be
  called while listening and null the connection. The ManagerWebsocket will need to abort/stop connecting/listening before closing to handle this
  gracefully.
* Move broker into its own class/interface instead of exposing all implementation details to the ochestration (AdapterCore)
* Implement handle config unit tests
* Setup test cases, see if there's a mocking solution for the server. Found a local implementation for Kestral.
* Finalize running java reference project with Java Adapter + Java Sut
* Implement & test c# connect to Mocking & Axini
* Implement handler start unit test


Activities
----------
- 14.11.24: Finalize first pass code documentation.
- 12.11.24: Clean up managedsocket. Tested under Ubuntu 24.
- 10.11.24: Adressed all different green paths and red paths, added readme
- 07.11.24: Moved broker to its own class
- 03.11.24: Fixing unix time. Seems the app passes a green path test.
- 01.11.24: Add config unit test, clean up warnings, handler start/stop integration test
- 31.10.24: Implemented Connect Integration tests
- 30.10.24: Websocket behaviour has test cases
- 23.10.24: First pass implementation done. This code warrants a mermaid sequence diagram...
