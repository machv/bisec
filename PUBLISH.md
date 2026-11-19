# Releasing to NuGet

## Prerequisities
```
nuget SetApiKey <api key>
```

if needed, set default push source:
```
nuget config -set DefaultPushSource=https://api.nuget.org/v3/index.json
```

## Publish new version

The version is set in `src/bisec/bisec.csproj` (`Version` and `PackageVersion`). Building the
library creates the package in `src/bisec/bin/<configuration>`. `src/harness` and `src/tests`
are not packed.

0.1.0-beta changes the public API; the README lists what callers have to adapt.

### Code sign
```
nuget sign BiSec.<version>.nupkg -CertificateFingerprint b7e269f571ae04305d379b916cafd05095260c8a -Timestamper http://timestamp.digicert.com
nuget sign BiSec.<version>.snupkg -CertificateFingerprint b7e269f571ae04305d379b916cafd05095260c8a -Timestamper http://timestamp.digicert.com
```

### Push to NuGet

it would push both package and symbols package
```
nuget push BiSec.<version>.nupkg
```
