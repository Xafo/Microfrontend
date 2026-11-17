![Oracle Logo](https://raw.githubusercontent.com/oracle/dotnet-db-samples/master/images/oracle-nuget.png) 
# Oracle.ManagedDataAccess.Core 23.26.401
Release Notes for Oracle Data Provider for .NET Core NuGet Package

November 2026

Oracle Data Provider for .NET (ODP.NET) Core features optimized ADO.NET data access to the Oracle database for .NET (Core) runtime. ODP.NET allows developers to take advantage of advanced Oracle database functionality, including AI vectors, Real Application Clusters, Application Continuity, JSON Relational Duality, and Fast Connection Failover. 

This document provides information that supplements the [Oracle Data Provider for .NET (ODP.NET) documentation](https://docs.oracle.com/en/database/oracle/oracle-database/26/odpnt/index.html).

## Oracle .NET Links
* [Oracle .NET Home Page](https://www.oracle.com/database/technologies/appdev/dotnet.html)
* [GitHub - Sample Code](https://github.com/oracle/dotnet-db-samples)
* [ODP.NET Discussion Forum](https://forums.oracle.com/ords/apexds/domain/dev-community/category/odp-dot-net)
* [YouTube](https://www.youtube.com/user/OracleDOTNETTeam)
* [X (Twitter)](https://x.com/oracledotnet)
* [Email Newsletter Sign Up](https://go.oracle.com/LP=28277?elqCampaignId=124071&nsl=onetdev)

## New Features
* No new features.

## Breaking Changes

Breaking change when running ODP.NET Core on the .NET 11 runtime: Conversions between .NET decimal and binary floating-point types (float and double) now produce correctly rounded results. Conversion results may therefore differ from those produced on earlier .NET versions, affecting values used in comparisons, calculations, or serialized output. Review code and tests that depend on specific conversion results. See Microsoft’s [.NET 11 breaking-change guidance](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/11/decimal-biginteger-floating-point-conversions).

## Bug Fixes in Oracle.ManagedDataAccess.Core 23.26.401
Bug 40197908 - ORACLEDECIMAL(DECIMAL) CONSTRUCTOR LOSES PRECISION WITH .NET 11 RUNTIME
Bug 40220704 - ORACLEINTERVALDS(DOUBLE) AND ORACLETIMSTAMP.ADDSECONDS(DOUBLE) LOSES PRECISION WITH .NET 11 RUNTIME
Bug 40254994 - DECIMAL PARAMETER VALUES LOSE PRECISION WHEN BOUND AS ORACLEDBTYPE ENUMS BINARYDOUBLE, BINARYFLOAT, DOUBLE, OR INTERVALDS WITH .NET 11 RUNTIME
Bug 40234339 - DECIMAL VALUES IN UDT LOSE PRECISION WHEN CONVERTED TO/FROM ORACLE BINARY_DOUBLE/BINARY_FLOAT WITH .NET 11 RUNTIME

 Copyright (c) 2024, 2026, Oracle and/or its affiliates. 
