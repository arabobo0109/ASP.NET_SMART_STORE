﻿using System.Collections.Generic;
using Smartstore.Collections;

namespace Smartstore.Forums.Models.Public
{
    public partial class ActiveDiscussionsModel
    {
        public bool IsActiveDiscussionsPage { get; set; }
        public bool ActiveDiscussionsFeedEnabled { get; set; }
        public int PostsPageSize { get; set; }

        public IPagedList<PublicForumTopicModel> ForumTopics { get; set; }
    }
}
