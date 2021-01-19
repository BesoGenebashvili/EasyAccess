using System;
using System.Data;

namespace EasyAccess.Samples.Console.Entities
{
    public class BaseEntity
    {
        [IdColumn]
        public int ID { get; set; }

        [Column(SqlDbType.DateTime)]
        public DateTime? DateCreated { get; set; }

        [Column(SqlDbType.NVarChar)]
        public string CreatedBy { get; set; }

        [Column(SqlDbType.DateTime)]
        public DateTime? LastUpdateDate { get; set; }

        [Column(SqlDbType.NVarChar)]
        public string LastUpdateBy { get; set; }
    }
}
