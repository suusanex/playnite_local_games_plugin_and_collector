#!/usr/bin/env dotnet
#:property TargetFramework=net10.0
#:property PublishAot=false
#:project ../LocalGameCatalog.Core/LocalGameCatalog.Core.csproj
#:include DlsiteIds.cs
#:include CsvKeys.cs
#:include DlsiteProductParser.cs
#:include DlsiteHttpFetcher.cs
#:include CollectorApp.cs

return DlsiteCollector.CollectorApp.Run(args);
