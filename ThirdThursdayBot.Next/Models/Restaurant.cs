using Newtonsoft.Json;
using System;

namespace ThirdThursdayBot.Models
{
    public class Restaurant
    {
        [JsonProperty("Location")]
        public string Location { get; set; }

        [JsonProperty("PickedBy")]
        public string PickedBy { get; set; }

        [JsonProperty("Date")]
        public DateTime Date { get; set; }
    }
}