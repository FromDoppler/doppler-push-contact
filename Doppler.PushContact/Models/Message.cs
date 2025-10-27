using Doppler.PushContact.Models.Models;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Doppler.PushContact.Models
{
    public class Message
    {
        [Required]
        public string Title { get; set; }

        [Required]
        public string Body { get; set; }

        public string OnClickLink { get; set; }

        public string ImageUrl { get; set; }

        public string IconUrl { get; set; }

        public bool PreferLargeImage { get; set; }

        public List<MessageAction> Actions { get; set; }
    }
}
