# Conversion of ThirdThursday Bot to Bot Framework SDK 4.0 and LUIS


To generate Recognizer result code, use the LuisGen tool:

1. Export the LUIS model to a Json document from the LUIS portal(https://luis.ai)
2. Install the LUISGen tool from https://github.com/microsoft/botbuilder-tools (specifically: https://github.com/Microsoft/botbuilder-tools/tree/master/packages/LUISGen")
3. After intalling the Node.js module, run: 

	luisgen luismodel.json -cs ThirdThursdayBot.LuisLunchRecognizerResult

	For mor information see: https://docs.microsoft.com/en-us/azure/bot-service/bot-builder-howto-v4-luisgen?view=azure-bot-service-4.0
