[API contracts](api-contracts.http)

# Add new push contact

```mermaid
sequenceDiagram
  participant User
  participant Browser
  participant PushScript
  participant Firebase
  participant PushContactApi
  participant PushApi
  participant MongoDb

User->>Browser: get page
activate Browser
Browser->>PushScript: exec
deactivate Browser
activate PushScript
PushScript->>+Firebase: requestPermision (popup)
Firebase-->>-PushScript: FCM: permission granted
PushScript->>Firebase: get device token
Firebase-->>PushScript: device token
PushScript->>+PushContactApi: POST /push-contacts
PushContactApi->>+PushApi: GET /devices/{token}
PushApi-->>-PushContactApi: device token information
PushContactApi->>MongoDb: store push contact
PushContactApi-->>-PushScript: add push contact response
PushScript-->>Browser: device token
deactivate PushScript

User->>Browser: complete email
activate Browser
Browser->>PushScript: email
deactivate Browser
activate PushScript
PushScript->>+PushContactApi: PUT /push-contacts/{deviceToken}/email
PushContactApi->>MongoDb: update push contact email
PushContactApi-->>-PushScript: update push contact email response
deactivate PushScript
```

# Send push notification from Doppler by domain

```mermaid
sequenceDiagram
  participant DopplerUser
  participant Doppler
  participant PushContactApi
  participant PushContactApiWorker
  participant MongoDb
  participant PushApi
DopplerUser->>+Doppler: send push notification to specific domain
Doppler->>+PushContactApi: POST /push-contacts/{domain}/message
PushContactApi-x+PushContactApiWorker: send async
PushContactApi-->>-Doppler: messageId
Doppler-->>-DopplerUser: shipment in progress!

PushContactApiWorker->>+MongoDb: get device tokens by domain
MongoDb-->>-PushContactApiWorker: device tokens
PushContactApiWorker->>+PushApi: POST /message
PushApi-->>-PushContactApiWorker: shipping results
PushContactApiWorker->>MongoDb: delete not valid push contacts
PushContactApiWorker->>MongoDb: add push contacts history events
PushContactApiWorker->>MongoDb: add sent message details
deactivate PushContactApiWorker
```

# Enable/disable push feature in specific domain from Doppler

```mermaid
sequenceDiagram
  participant DopplerUser
  participant Doppler
  participant PushContactApi
  participant MongoDb
DopplerUser->>+Doppler: enable/disable push feature in specific domain
Doppler->>+PushContactApi: PUT /domains/{name}
PushContactApi->>+MongoDb: upsert domain
PushContactApi-->>-Doppler: success
Doppler-->>-DopplerUser: done!
```

# Get push feature status by domain name

```mermaid
sequenceDiagram
  participant ApiConsumer
  participant PushContactApi
  participant MongoDb
ApiConsumer->>+PushContactApi: GET /domains/{name}/isPushFeatureEnabled
PushContactApi->>+MongoDb: get push feature status by domain
PushContactApi-->>-ApiConsumer: push feature status
```

# Get Automation report

```mermaid
sequenceDiagram
  participant DopplerUser
  participant Doppler
  participant PushContactApi
  participant MongoDb
DopplerUser->>+Doppler: get Automation report
loop for each sent message
  Doppler->>+PushContactApi: GET push-contacts/{domain}/messages/{messageId}/details
  PushContactApi->>+MongoDb: get message details
  MongoDb->>-PushContactApi: message details
  PushContactApi-->>-Doppler: message details
end
Doppler->>+DopplerUser: Automation report
```
