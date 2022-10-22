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

### Code sign
```
nuget sign BiSec.<version>.nupkg -CertificateFingerprint b7e269f571ae04305d379b916cafd05095260c8a -Timestamper http://timestamp.digicert.com
nuget sign BiSec.<version>.snupkg -CertificateFingerprint b7e269f571ae04305d379b916cafd05095260c8a -Timestamper http://timestamp.digicert.com
```

### Push to NuGet

it would push both package and symbols package
```
nuget push BiSec.0.0.3-beta.nupkg
```
