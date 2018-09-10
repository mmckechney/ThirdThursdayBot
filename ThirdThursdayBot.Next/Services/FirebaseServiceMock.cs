using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ThirdThursdayBot.Models;

namespace ThirdThursdayBot.Services
{
    public class FirebaseServiceMock : IFirebaseService
    {
        private HttpClient _client;

        public FirebaseServiceMock(string firebaseEndpoint)
        {

        }

        public async Task<Restaurant[]> GetAllVisitedRestaurantsAsync()
        {
            Restaurant[] rest = new Restaurant[5] {
                new Restaurant(){ Location = "Wendys", Date = new DateTime(2019,03,15), PickedBy = "Mike" },
                new Restaurant(){ Location = "Burger King", Date = new DateTime(2019,04,19), PickedBy = "Trey" },
                new Restaurant(){ Location = "McDonalds", Date = new DateTime(2019,05,17), PickedBy = "Anthony" },
                new Restaurant(){ Location = "Taco Bell", Date = new DateTime(2019,06,21), PickedBy = "Mike" },
                new Restaurant(){ Location = "Pizza Hut", Date = new DateTime(2019,07,19), PickedBy = "Trey" } };

            return await Task.FromResult(rest);

            //return JsonConvert.DeserializeObject<Restaurant[]>(json);
        }

        public async Task<Restaurant> GetLastVisitedRestaurantAsync()
        {
            var restaurants = await GetAllVisitedRestaurantsAsync();
            return restaurants.Last();
            //return restaurants.LastOrDefault();
        }

        public async Task<string> GetPreviouslyVisitedRestaurantsMessageAsync()
        {
            try
            {
                var restaurants = await GetAllVisitedRestaurantsAsync();

                var message = new StringBuilder(Messages.RestaurantListingMessage);
                foreach (var restaurant in restaurants)
                {
                    message.AppendLine($"- '{restaurant.Location}' on {restaurant.Date.ToString("M/d/yyyy")} ({restaurant.PickedBy})");
                }

                return message.ToString();
            }
            catch
            {
                return Messages.DatabaseAccessIssuesMessage;
            }
        }

        public async Task<string[]> GetAllMembersAsync()
        {
            var members = new string[3] { "Mike", "Trey", "Anthony" };
            return await Task.FromResult(members);
            //var json = await _client.GetStringAsync("/Members/.json");

            // return JsonConvert.DeserializeObject<string[]>(json);
        }

    }
}