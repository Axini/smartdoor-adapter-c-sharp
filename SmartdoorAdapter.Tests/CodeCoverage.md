Running code coverage
=====================

1: Install dotnet-coverage

```shell
dotnet tool install --global dotnet-coverage
```

2. Collect data

```shell
dotnet-coverage collect "dotnet test --settings CodeCoverage.runsettings" --output-format xml --output coverage-report.xml
```

3. Install report generator

```shell
dotnet tool install --global dotnet-reportgenerator-globaltool
```

4. Create the report

```shell
reportgenerator "-reports:output.xml" "-targetdir:coverage-report" -reporttypes:Html
```