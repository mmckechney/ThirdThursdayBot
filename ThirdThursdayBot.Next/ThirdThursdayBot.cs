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

namespace ThirdThursdayBot.Next
{
    public class ThirdThursdayBot : IBot
    {
        private readonly IFirebaseService _service;
        private readonly IYelpService _yelpService;
        public ThirdThursdayBot(IConfiguration configuration)
        {
            //_service = new FirebaseService(configuration.GetSection("DatabaseEndpoint")?.Value);
            _service = new FirebaseServiceMock(configuration.GetSection("DatabaseEndpoint")?.Value);
            _yelpService = new YelpService(
                configuration.GetSection("YelpClientId")?.Value,
                configuration.GetSection("YelpClientSecret")?.Value,
                configuration.GetSection("YelpPreferredLocation")?.Value
            );
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
                var message = activity.Text;

                if (Regex.IsMatch(message, "(?<=have we been to )(?<restaurant>[^?]+)", RegexOptions.IgnoreCase))
                {
                    var restaurant = Regex.Match(message, @"(?<=have we been to )(?<restaurant>[^?]+)", RegexOptions.IgnoreCase)?.Groups["restaurant"]?.Value ?? "";
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
                        await ReplyWithUnrecognizableRestaurantAsync(activity, context);
                    }
                }
                else if (Regex.IsMatch(message, "where should we go|recommendation|pick for me", RegexOptions.IgnoreCase))
                {
                    await ReplyWithRandomRestaurantRecommendation(activity, context);
                }
                else if (Regex.IsMatch(message, "show|all|list all", RegexOptions.IgnoreCase))
                {
                    await ReplyWithRestaurantListingAsync(activity, context);
                }
                else if (Regex.IsMatch(message, "who's next|who is next|whose (pick|turn) is it", RegexOptions.IgnoreCase))
                {
                    await ReplyWithNextMemberToChoose(activity, context);
                }
                else
                {
                    await ReplyWithDefaultMessageAsync(activity, context);
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
    }
}