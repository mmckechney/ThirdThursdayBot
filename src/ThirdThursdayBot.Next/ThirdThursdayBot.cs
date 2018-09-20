using System.Threading.Tasks;
using Microsoft.Bot;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Core.Extensions;
using Microsoft.Bot.Schema;
using System.Text.RegularExpressions;
using ThirdThursdayBot.Services;
using ThirdThursdayBot.Models;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System;
using System.Collections.Generic;
using Microsoft.Bot.Builder.Ai.LUIS;

namespace ThirdThursdayBot.Next
{
    public class ThirdThursdayBot : IBot
    {
        private readonly IFirebaseService _service;
        private readonly IYelpService _yelpService;

        private readonly LuisModel luisLunchModel;
        private readonly LuisRecognizer luisRecognizer;
        public ThirdThursdayBot(IConfiguration configuration)
        {
            //_service = new FirebaseService(configuration.GetSection("DatabaseEndpoint")?.Value);
            _service = new FirebaseServiceMock(configuration.GetSection("DatabaseEndpoint")?.Value);
            _yelpService = new YelpService(
                configuration.GetSection("YelpClientId")?.Value,
                configuration.GetSection("YelpClientSecret")?.Value,
                configuration.GetSection("YelpPreferredLocation")?.Value
            );

            var luidModelId = configuration.GetSection($"Luis-ModelId-Lunch")?.Value;
            var luisSubscriptionKey = configuration.GetSection("Luis-SubscriptionKey")?.Value;
            var luisUri = new Uri(configuration.GetSection("Luis-Url")?.Value);
            this.luisLunchModel = new LuisModel(luidModelId, luisSubscriptionKey, luisUri);
            this.luisRecognizer = new LuisRecognizer(luisLunchModel);
           
        }


        /// <summary>
        /// Every Conversation turn for our EchoBot will call this method. In here
        /// the bot checks the Activty type to verify it's a message, bumps the 
        /// turn conversation 'Turn' count, and then echoes the users typing
        /// back to them. 
        /// </summary>
        /// <param name="context">Turn scoped context containing all the data needed
        /// for processing this conversation turn. </param>        
        public async Task OnTurn(ITurnContext context)
        {
            var activity = context.Activity;
            if (activity.Type == ActivityTypes.Message)
            {
                var luisResult = await luisRecognizer.Recognize<LuisLunchRecognizerResult>(context.Activity.Text, System.Threading.CancellationToken.None);
                var topIntent = luisResult.TopIntent();
                var message = activity.Text;

                switch (topIntent.intent)
                {
                    case LuisLunchRecognizerResult.Intent.History:
                        var restaurant = luisResult.Entities.PastRestaurant?.FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(restaurant))
                        {
                            var vistedRestaurants = await _service.GetAllVisitedRestaurantsAsync();
                            var visitedRestaurant = vistedRestaurants.FirstOrDefault(r => string.Equals(r.Location, restaurant, StringComparison.OrdinalIgnoreCase));
                            if (visitedRestaurant != null)
                            {
                                await ReplyWithVisitedRestaurantAsync(visitedRestaurant, activity, context);
                            }
                            else
                            {
                                await ReplyWithUnchosenRestaurantAsync(restaurant, activity, context);
                            }
                        }
                        else
                        {
                            await ReplyWithRestaurantListingAsync(activity, context);
                        }
                        break;
                    case LuisLunchRecognizerResult.Intent.Suggestion:
                        await ReplyWithRandomRestaurantRecommendation(activity, context);
                        break;
                    case LuisLunchRecognizerResult.Intent.WhosNext:
                        await ReplyWithNextMemberToChoose(activity, context);
                        break;
                    case LuisLunchRecognizerResult.Intent.None:
                        await ReplyWithDefaultMessageAsync(activity, context);
                        break;

                }
                
            }
        }



        private async Task ReplyWithNextMemberToChoose(Activity activity, ITurnContext context)
        {
            try
            {
                var lastRestaurantVisited = await _service.GetLastVisitedRestaurantAsync();
                var members = await _service.GetAllMembersAsync();

                var currentMember = Array.IndexOf(members, lastRestaurantVisited?.PickedBy ?? "");
                var nextMember = members[(currentMember + 1) % members.Length];
                var nextMonth = lastRestaurantVisited?.Date.AddMonths(1) ?? DateTime.Now.AddMonths(1);

                var replyMessage = string.Format(Messages.NextChooserFormattingMessage, nextMember, nextMonth.ToString("MMMM"));
                var reply = activity.CreateReply(replyMessage);
                await context.SendActivity(reply);
            }
            catch
            {
                var reply = activity.CreateReply("I'm not sure who has the next pick. Try again later.");
                await context.SendActivity(reply);
            }
        }

        private async Task ReplyWithDefaultMessageAsync(Activity activity, ITurnContext context)
        {
            var reply = activity.CreateReply(Messages.DefaultResponseMessage);
            await context.SendActivity(reply);
        }

        private async Task ReplyWithVisitedRestaurantAsync(Restaurant restaurant, Activity activity, ITurnContext context)
        {
            var replyMessage = string.Format(Messages.PreviouslyChosenResturantFormattingMessage, restaurant.Location, restaurant.PickedBy, restaurant.Date);
            var reply = activity.CreateReply(replyMessage);

            await context.SendActivity(reply);
        }

        private async Task ReplyWithUnchosenRestaurantAsync(string restaurant, Activity activity, ITurnContext context)
        {
            var replyMessage = string.Format(Messages.UnchosenRestaurantFormattingMessage, restaurant);
            var reply = activity.CreateReply(replyMessage);

            await context.SendActivity(reply);
        }

        private async Task ReplyWithUnrecognizableRestaurantAsync(Activity activity, ITurnContext context)
        {
            var reply = activity.CreateReply(Messages.UnrecognizableRestaurantMessage);

            await context.SendActivity(reply);
        }

        private async Task ReplyWithRestaurantListingAsync(Activity activity, ITurnContext context)
        {
            var replyMessage = await _service.GetPreviouslyVisitedRestaurantsMessageAsync();
            var reply = activity.CreateReply(replyMessage);

            await context.SendActivity(reply);
        }

        private async Task ReplyWithRandomRestaurantRecommendation(Activity activity, ITurnContext context)
        {
            try
            {
                var previouslyVisistedRestaurants = await _service.GetAllVisitedRestaurantsAsync();
                var recommendation = await _yelpService.GetRandomUnvisitedRestaurantAsync(previouslyVisistedRestaurants);

                var recommendationMessage = activity.CreateReply(GetFormattedRecommendation(recommendation));
                await context.SendActivity(recommendationMessage);
            }
            catch
            {
                var failedMessage = activity.CreateReply(Messages.UnableToGetRecommendationMessage);
                await context.SendActivity(failedMessage);
            }
        }

        private string GetFormattedRecommendation(YelpBusiness choice)
        {
            return string.Format(Messages.RecommendationFormattingMessage,
                choice.Name,
                choice.Rating,
                choice.Location.FullAddress,
                choice.PhoneNumber);
        }

        #region 
        private static async Task<(IEnumerable<string> intents, IEnumerable<string> entities)> RecognizeUtteranceAsync(LuisModel luisModel, string text)
        {
            var luisRecognizer = new LuisRecognizer(luisModel);
            var recognizerResult = await luisRecognizer.Recognize(text, System.Threading.CancellationToken.None);

            // list the intents
            var intents = new List<string>();
            foreach (var intent in recognizerResult.Intents)
            {
                intents.Add($"{intent.Key}: {intent.Value}");
            }

            // list the entities
            var entities = new List<string>();
            foreach (var entity in recognizerResult.Entities)
            {
                if (!entity.Key.ToString().Equals("$instance"))
                {
                    entities.Add($"{entity.Key}: {entity.Value.First}");
                }
            }

            return (intents, entities);
        }

        #endregion

    }
}
