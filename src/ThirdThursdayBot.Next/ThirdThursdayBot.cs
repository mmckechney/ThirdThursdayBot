using AdaptiveCards;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.AI.Luis;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ThirdThursdayBot.Models;
using ThirdThursdayBot.Services;

namespace ThirdThursdayBot.Next
{
    public class ThirdThursdayBot : Microsoft.Bot.Builder.IBot
    {
        private readonly IFirebaseService _service;
        private readonly IYelpService _yelpService;

        private readonly LuisApplication luisLunchModel;
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
            var luisUri = configuration.GetSection("Luis-Url")?.Value;
            this.luisLunchModel = new LuisApplication(luidModelId, luisSubscriptionKey, luisUri);
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
        public async Task OnTurnAsync(ITurnContext context, CancellationToken cancellationToken = default(CancellationToken))
        {
            var activity = context.Activity;
            if (activity.Type == ActivityTypes.Message)
            {
                var generic = await luisRecognizer.RecognizeAsync(context, System.Threading.CancellationToken.None);
                var luisResult = await luisRecognizer.RecognizeAsync<LuisLunchRecognizerResult>(context, System.Threading.CancellationToken.None);
                var topIntent = luisResult.TopIntent();
                var message = activity.Text;

                switch (topIntent.intent)
                {
                    case LuisLunchRecognizerResult.Intent.History:
                        var restaurant = luisResult.Entities.Restaurant?.FirstOrDefault();
                        
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
                        var starRating = luisResult.Entities.StarRating?.FirstOrDefault();
                        await ReplyWithRandomRestaurantRecommendation(starRating, activity, context);
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
                await context.SendActivityAsync(reply);
            }
            catch
            {
                var reply = activity.CreateReply("I'm not sure who has the next pick. Try again later.");
                await context.SendActivityAsync(reply);
            }
        }

        private async Task ReplyWithDefaultMessageAsync(Activity activity, ITurnContext context)
        {
            var reply = activity.CreateReply(Messages.DefaultResponseMessage);
            await context.SendActivityAsync(reply);
        }

        private async Task ReplyWithVisitedRestaurantAsync(Restaurant restaurant, Activity activity, ITurnContext context)
        {

            AdaptiveCard card = new AdaptiveCard();
            card.Speak = $"You have already been to {restaurant.Location}";


            card.Body.Add(new AdaptiveTextBlock()
            {
                Text = $"You have already been to {restaurant.Location}.",
                Weight = AdaptiveTextWeight.Default

            });

            var set = new AdaptiveFactSet();
            set.Facts.Add(new AdaptiveFact()
            {
                Title = "Chosen By",
                Value = restaurant.PickedBy

            });
            set.Facts.Add(new AdaptiveFact()
            {
                Title = "On",
                Value = restaurant.Date.ToShortDateString()

            });
            card.Body.Add(set);

            var attachment = new Attachment()
            {
                Content = card,
                ContentType = "application/vnd.microsoft.card.adaptive",
                Name = "AlreadyChosen"
            };


            var reply = activity.CreateReply();
            reply.Attachments = new List<Attachment>() { attachment };
            await context.SendActivityAsync(reply);
           
        }

        private async Task ReplyWithUnchosenRestaurantAsync(string restaurant, Activity activity, ITurnContext context)
        {
            var reply = activity.CreateReply();
            var details = await _yelpService.GetRestaurantDetailsAsync(restaurant);
            if (details != null)
            {
                string msg;
                if (details.Name.ToLowerInvariant().IndexOf(restaurant.ToLowerInvariant().Trim()) == -1)
                {
                    msg = $"I couldn't find <b>{restaurant}</b>, but <b>{details.Name}</b> looks like it might be good";
                }
                else
                {
                    msg = $"Sure thing <b>{details.Name}</b> sounds great!";
                }

                AdaptiveCard card = new AdaptiveCard();
                card.Speak = msg;
                card.Body.Add(new AdaptiveTextBlock()
                {
                    Text = msg,
                    Weight = AdaptiveTextWeight.Default,
                    Wrap = true
                });

                AppendRestaurantDetailsCard(ref card, details);
                
                var attachment = new Attachment()
                {
                    Content = card,
                    ContentType = "application/vnd.microsoft.card.adaptive",
                    Name = "New Restaurant Card"
                };

                reply.Attachments.Add(attachment);
                
            }
            else
            {
            }

            

            await context.SendActivityAsync(reply);
        }

        private async Task ReplyWithRestaurantListingAsync(Activity activity, ITurnContext context)
        {
            var replyMessage = await _service.GetPreviouslyVisitedRestaurantsMessageAsync();
            var reply = activity.CreateReply(replyMessage);

            await context.SendActivityAsync(reply);
        }

        private async Task ReplyWithRandomRestaurantRecommendation(string starRating, Activity activity,  ITurnContext context)
        {

            try
            {
                var previouslyVisistedRestaurants = await _service.GetAllVisitedRestaurantsAsync();
                var recommendation = await _yelpService.GetRandomUnvisitedRestaurantAsync(previouslyVisistedRestaurants, starRating);
                if (recommendation == null)
                {
                    double x;
                    if (double.TryParse(starRating, out x))
                    {
                        starRating = starRating + " star";
                    }
                    await context.SendActivityAsync($"Sorry, I couldn't find a {starRating} place. Can you try a different rating?");

                }
                else
                {
                    var recommendationAttachment = GetFormattedRecommendation(recommendation);

                    var recommendationMessage = activity.CreateReply();
                    recommendationMessage.Attachments = new List<Attachment>() { recommendationAttachment };
                    await context.SendActivityAsync(recommendationMessage);
                }
            }
            catch
            {
                var failedMessage = activity.CreateReply(Messages.UnableToGetRecommendationMessage);
                await context.SendActivityAsync(failedMessage);
            }
        }

        private Attachment GetFormattedRecommendation(YelpBusiness choice)
        {

            AdaptiveCard card = new AdaptiveCard();
            card.Speak = $"How about trying {choice.Name}";


            card.Body.Add(new AdaptiveTextBlock()
            {
                Text = $"Here's a recommendation for you",
                Weight = AdaptiveTextWeight.Bolder

            });

            AppendRestaurantDetailsCard(ref card, choice);


            var attachment = new Attachment()
            {
                Content = card,
                ContentType = "application/vnd.microsoft.card.adaptive",
                Name = "Recommendation Card"
            };

            return attachment;

   

        }

        #region Adaptive Card Helpers
        private void AppendRestaurantDetailsCard(ref AdaptiveCard card, YelpBusiness details)
        {
            var stdElements = StandardRestaurantCardElements(details);
            var choiceActions = StandardRestaurantChoiceElements(details.YelpSearchUrl);

            card.Body.AddRange(stdElements);
            card.Actions.AddRange(choiceActions);
        }
        private List<AdaptiveElement> StandardRestaurantCardElements(YelpBusiness restaurant)
        {
            List<AdaptiveElement> lst = new List<AdaptiveElement>();
            lst.Add(new AdaptiveTextBlock()
            {
                Text = restaurant.Name,
                Separator = true

            });
            lst.Add(new AdaptiveTextBlock()
            {
                Text = restaurant.Location.FullAddress,
                Separator = false

            });
            lst.Add(new AdaptiveTextBlock()
            {
                Text = $"Phone: {restaurant.PhoneNumber}",
                Separator = false

            });
            lst.Add(new AdaptiveTextBlock()
            {
                Text = $"Start rating: {restaurant.Rating}",
                Separator = false

            });

            lst.Add(new AdaptiveImage(restaurant.Image)
            {

                Separator = true,
                Size = AdaptiveImageSize.Auto

            });

            return lst;

        }
        private List<AdaptiveAction> StandardRestaurantChoiceElements(string yelpUrl)
        {
            List<AdaptiveAction> lst = new List<AdaptiveAction>();

            lst.Add(new AdaptiveOpenUrlAction()
            {
                Url = new Uri(yelpUrl),
                Title = "More Information ..."
            });

            lst.Add(new AdaptiveSubmitAction()
            {
                  Title = "Pick this restaurant",
                  Data = "Pick this restaurant"

            });


            lst.Add(new AdaptiveSubmitAction()
            {
                Title = "Suggest somewhere else",
                Data = "Suggest somewhere else"
            });



            return lst;

        }

        #endregion

    }
}
