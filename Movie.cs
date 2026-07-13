using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramMovieBot.Models
{
    public class Movie
    {
        public int Id { get; set; }

        public string MovieCode { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string FileId { get; set; } = string.Empty;

        public string? Description { get; set; }
        public string MediaType { get; set; } = string.Empty;
        public string? PhotoFileId { get; set; }
        public int Views { get; set; }
        public int CategoryId { get; set; }
    }
}