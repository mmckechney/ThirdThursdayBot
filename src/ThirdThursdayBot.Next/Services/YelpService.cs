using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using ThirdThursdayBot.Models;

namespace ThirdThursdayBot.Services
{
    public class YelpService : IYelpService
    {
        private const string YelpSearchUrl = "https://api.yelp.com/v3/businesses/search?";

        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _preferredLocation;
        private string _authToken;

        public YelpService(string clientId, string clientSecret, string preferredLocation = "Lake Charles")
        {
            _clientId = clientId;
            _clientSecret = clientSecret;
            _preferredLocation = preferredLocation;
        }

        /// <summary>
        /// Gets a random, unvisited Restauraunt from Yelp's API
        /// </summary>
        public async Task<YelpBusiness> GetRandomUnvisitedRestaurantAsync(Restaurant[] restaurantsToExclude, string starRating)
        {
            var (low,high) = InterpretStarRating(starRating);
            try
            {
                using (var yelpClient = new HttpClient())
                {
                    yelpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_clientSecret}");
                    var response = await GetYelpSearchQueryAsync(yelpClient);
                    var recommendation = response.Restaurants
                                                  .Where(r => r.Rating >= low && r.Rating <= high)
                                                 .OrderBy(r => Guid.NewGuid())
                                                 .First(r => restaurantsToExclude.All(v => !v.Location.Contains(r.Name) && !r.Name.Contains(v.Location)));

                    return recommendation;
                }
            }
            catch(Exception exe)
            {
                // Something else bad happened when communicating with Yelp; If you like logging, you should probably do that here
                return null;
            }
        }
        private (double,double) InterpretStarRating(string starRating)
        {
            if (starRating == null)
                return (2.0,5.0);

            double rating;
            if(double.TryParse(starRating, out rating))
            {
                return (rating,rating);
            }
            switch(starRating.ToLowerInvariant())
            {
                case "excellent":
                case "great":
                case "awesome":
                case "classy":
                case "superb":
                    return (4.0,5.0);
                case "good":
                case "decent":
                    return (3.0,4.0);
                case "OK":
                case "Fair":
                default:
                    return (2.0,3.0);
            }
        }

        public async Task<YelpBusiness> GetRestaurantDetailsAsync(string restaurantName)
        {
            try
            {
                using (var yelpClient = new HttpClient())
                {
                    yelpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_clientSecret}");
                    var response = await GetYelpSearchQueryAsync(yelpClient, restaurantName);
                    var recommendation = response.Restaurants.First();

                    return recommendation;
                }
            }
            catch (Exception exe)
            {
                // Something else bad happened when communicating with Yelp; If you like logging, you should probably do that here
                return null;
            }
        }

        /// <summary>
        /// Ensures that the Yelp API has been authenticated for the current request
        /// </summary>
        private async Task EnsureYelpAuthenticationAsync(HttpClient yelpClient)
        {
            if (string.IsNullOrWhiteSpace(_authToken))
            {
                var authenticationResponse = await yelpClient.PostAsync($"https://api.yelp.com/oauth2/token?client_id={_clientId}&client_secret={_clientSecret}&grant_type=client_credentials", null);
                if (authenticationResponse.IsSuccessStatusCode)
                {
                    var authResponse = JsonConvert.DeserializeObject<YelpAuthenticationResponse>(await authenticationResponse.Content.ReadAsStringAsync());
                    _authToken = authResponse.AccessToken;
                }
            }
        }

        private async Task<YelpSearchResponse> GetYelpSearchQueryAsync(HttpClient yelpClient, string restaurantName)
        {
            if (string.IsNullOrWhiteSpace(restaurantName))
            {
                restaurantName = "food";
            }
            var searchTerms = new[]
            {
                $"term={restaurantName}",
                $"location={_preferredLocation}",
                $"limit=50"
            };

            var searchRequest = await yelpClient.GetStringAsync($"{YelpSearchUrl}{string.Join("&", searchTerms)}");
            return JsonConvert.DeserializeObject<YelpSearchResponse>(searchRequest);
        }
        /// <summary>
        /// Sets the headers and search terms for the Yelp search query
        /// </summary>
        private async Task<YelpSearchResponse> GetYelpSearchQueryAsync(HttpClient yelpClient)
        {
            return await GetYelpSearchQueryAsync(yelpClient, null);
        }
    }
}