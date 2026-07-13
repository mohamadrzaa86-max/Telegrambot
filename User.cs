using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramMovieBot.Models
{
    public class User
    {
        public long TelegramId { get; set; }

        public string? Username { get; set; }

        public string? FirstName { get; set; }
        public int Id { get; internal set; }
    }
}