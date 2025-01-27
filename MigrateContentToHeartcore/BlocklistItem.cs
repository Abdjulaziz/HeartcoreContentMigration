using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MigrateContentToHeartcore
{
    internal class MyBlocklistItem
    {
        public MyContent content { get; set; }
    }
    internal class MyContent
    {
        public string contentTypeAlias { get; set; }
        public string title {  get; set; }
        public string indexNumber {  get; set; }
    }
}
