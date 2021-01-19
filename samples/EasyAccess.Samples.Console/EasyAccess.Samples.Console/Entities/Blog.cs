using EasyAccess.Samples.Console.Types;
using System.Collections.Generic;
using System.Data;

namespace EasyAccess.Samples.Console.Entities
{
    public class Blog : BaseEntity
    {
        [Column(SqlDbType.Int)]
        public int ApplicationUserID { get; set; }

        [Column(SqlDbType.NVarChar)]
        public string Title { get; set; }

        [Column(SqlDbType.NVarChar)]
        public string ShortDescription { get; set; }

        [TypeColumn(SqlDbType.NVarChar)]
        public HTMLContent HTMLContent { get; set; }

        [Column(SqlDbType.Int)]
        public int EstimatedReadingTimeInMinutes { get; set; }

        [Column(SqlDbType.VarBinary)]
        public byte[] Cover { get; set; }
    }
}
