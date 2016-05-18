using System;
using System.Collections.Generic;

namespace DinnerParty.Models
{
    public class PageModel
    {
        public string PreFixTitle { get; set; }
        public string Title { get; set; }
        public bool IsAuthenticated { get; set; }
        public string CurrentUser { get; set; }
        public List<ErrorModel> Errors { get; set; }
    }
}