using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramMovieBot.Models
{
    public class AdminState
    {
        public string? FileId { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }

        public string? PhotoFileId { get; set; }
        public bool WaitingForDeleteCode { get; set; }
        public bool WaitingForEditCode { get; set; }
        public Movie? EditingMovie { get; set; }
        public string? Mode { get; set; }
        public bool WaitingForBroadcast { get; set; }
      

        public bool WaitingForMovie { get; set; }
        public bool WaitingForTitle { get; set; }
        public bool WaitingForDescription { get; set; }
        public bool WaitingForChannelUsername { get; set; }
    }
}
