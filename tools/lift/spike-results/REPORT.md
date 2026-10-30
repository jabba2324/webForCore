# Compile errors: 5 unique across 1 project(s)

| Project | Errors | Distinct codes | Distinct missing APIs |
| --- | --- | --- | --- |
| System.Web.WebPages.Deployment.csproj | 5 | 4 | 3 |

## Most common missing APIs (all projects)

| Count | Symbol | Projects |
| --- | --- | --- |
| 1 | `AppDomainSetup.ConfigurationFile` | System.Web.WebPages.Deployment |
| 1 | `AppDomainSetup.PrivateBinPath` | System.Web.WebPages.Deployment |
| 1 | `AppDomain.Evidence` | System.Web.WebPages.Deployment |

## System.Web.WebPages.Deployment.csproj

Codes: CS0117 x2, CS1729 x1, CS0200 x1, CS1061 x1

Worst files:

- 5 `System.Web.WebPages.Deployment/AppDomainHelper.cs`

Top missing APIs:

- 1 `AppDomainSetup.ConfigurationFile`
- 1 `AppDomainSetup.PrivateBinPath`
- 1 `AppDomain.Evidence`
