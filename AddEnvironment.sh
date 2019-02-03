#!/usr/bin/env bash

ENV=""
INSTANCE=""
PASSWORD=""
TENANT=""

# Create resource group
az group create -n GameATron4000Environment-$ENV -l westeurope

# Register AAD application
APP_JSON=$(az ad app create --display-name GameATron4000Environment-$ENV-$INSTANCE --identifier-uris http://$TENANT/GameATron4000/$ENV/$INSTANCE --password $PASSWORD --available-to-other-tenants true)
APP_ID=$( jq -r '.appId' <<< "${APP_JSON}" )

# Create bot registration
az bot create --kind registration --name GameATron4000-$ENV-$INSTANCE --appid $APP_ID --password $PASSWORD --endpoint https://todo --sku F0 -g GameATron4000Environment-$ENV

# Add DirectLine channel
az bot directline create --name GameATron4000-$ENV-$INSTANCE -g GameATron4000Environment-$ENV
