# Publication authorization template (not an authorization)

The live publication path requires a separate owner-created file:

```
docs/reviews/dogwood-sharp-pub-prep-01/OWNER-AUTHORIZATION-PUBLICATION.json
```

That file must contain, at minimum:

```json
{
  "authorizationId": "DOGWOOD-SHARP-PUB-PREP-01-PUBLICATION-<sequence>",
  "scope": "publication",
  "dryRunAuthorized": false,
  "publicationAuthorized": true,
  "inventoryId": "DOGWOOD-SHARP-PUB-PREP-01-INVENTORY-1",
  "inventorySha256": "524819fcea620889e45e62c5645a11fec0c8a01a7062973dd8c80bd3901bf252",
  "releaseCandidate": "58b0a3332391d85cdf19fd7b794d79516d2ecdfe",
  "sourceRunId": 46053558029,
  "environment": "nuget-publication",
  "destination": "https://api.nuget.org/v3/index.json",
  "packages": [
    {
      "file": "DogwoodSharp.0.1.0-preview.1.nupkg",
      "sha256": "384e013a236bfcb9b761a92ff861f0df54b2e8a736a5f2c1fb778268307b1911"
    },
    {
      "file": "DogwoodSharp.0.1.0-preview.1.snupkg",
      "sha256": "e35ae30ef409b44402f32361dcec800a57b7499cd610e2345773d6adafbad563"
    },
    {
      "file": "DogwoodSharp.CedarSharp.0.1.0-experimental.1.nupkg",
      "sha256": "3d882e6be062ed998cf05e485b06dee4868819785e22fdf702d5050ba306daa4"
    },
    {
      "file": "DogwoodSharp.CedarSharp.0.1.0-experimental.1.snupkg",
      "sha256": "49cefe0d69af687659c60decb141c56a1773b0583784586cd3ee09e5ea1803fd"
    }
  ],
  "authorizedBy": "<owner identity>",
  "authorizedAt": "<ISO-8601 date>",
  "note": "Authorizes exactly the four listed artifacts for one publication attempt."
}
```

The file is deliberately absent. The dry-run authorization
`OWNER-AUTHORIZATION-DRY-RUN.json` cannot be used for live publication; the
verifier requires `scope: "publication"` and `publicationAuthorized: true`.
The owner must create the live authorization only after deciding to publish.

Changing the inventory, release candidate, source run, destination,
environment or any artifact hash requires a new inventory and a new
authorization. The authorization is bound to the exact inventory SHA-256.
