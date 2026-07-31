# SplunkAPI
Simple Proxy API for Splunk Ingestion and Searches

## How and why
The ingestion endpoint exists simply because a certain service can send data to an api but doesn't support an authentication header which we need for specifying the splunk hec token.

The search endpoint might be more interesting. Splunk normally has an api for that however it can't be used in the free license. However, we can still run a search by running it directly on the splunk executable, so this is exactly what I do here. This allows simulating a lot of stuff that isn't possible otherwise in the free version, for example scheduled searches, alerts, etc.

## Installation
Either use the helm chart to deploy the api in kubernetes and adjust the values.yaml file to your needs or deploy the api manually. You need to specify the following environment variables:
```
SPLUNK_HEC_URL (string): The HEC url of your splunk indexer (Example: splunk.example.com:8088/services/collector/event)
SPLUNK_HEC_IGNORE_CERT_ERRORS (boolean): Ignores certificate errors when calling the HEC endpoint if the value is true
SPLUNK_SEARCH_API_TOKEN (string): Any value, this has to match when calling the search endpoint as a simple form of authentication
SPLUNK_SEARCH_PREVIEW (boolean): Enables or disables the search preview
SPLUNK_SEARCH_MAXOUT (integer): Maximum number of events that can be returned by the search endpoint
SPLUNK_SEARCH_MAXTIME (integer): Maximum number of seconds that a search can run before timing out
SPLUNK_RUNNING_IN_KUBERNETES (boolean): Should be set to true if splunk is running in kubernetes, if it runs natively, set it to false
SPLUNK_EXECUTABLE (string): Full path of the splunk executable (Example: /opt/splunk/bin/splunk)
SPLUNK_KUBERNETES_NAMESPACE (string): Namespace that contains the splunk pod in kubernetes (Can be ignored when SPLUNK_RUNNING_IN_KUBERNETES is false)
SPLUNK_KUBERNETES_POD (string): Splunk pod name in kubernetes (Can be ignored when SPLUNK_RUNNING_IN_KUBERNETES is false)
SPLUNK_KUBERNETES_POD_CONTAINER (string): Container that contains the splunk pod in kubernetes (Can be ignored when SPLUNK_RUNNING_IN_KUBERNETES is false)
```

## Examples
Running a search
```
curl -X 'POST' \
  'http://splunk-api.example.com/service/search?token=xxxxx&format=csv' \
  -H 'accept: */*' \
  -H 'Content-Type: text/plain' \
  -d 'index=example sourcetype=example:src | head 5 | table *'
```
Ingesting data
```
curl -X 'POST' \
  'http://splunk-api.example.com/service/ingest?token=xxxxx&index=test&sourcetype=test%3Asrc' \
  -H 'accept: */*' \
  -H 'Content-Type: application/json' \
  -d '{"something": "yes"}'
```
