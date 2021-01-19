using System.Collections.Generic;
using EasyAccess.Samples.Console.Entities;

namespace EasyAccess.Samples.Console.DataAccess
{
    public class BlogRepository
    {
        private readonly EasyAccess _easyAccess;

        public BlogRepository()
        {
            _easyAccess = EasyAccess.Create("Server=(localdb)\\MSSQLLocalDB;Database=BlogEngine;Trusted_Connection=True;MultipleActiveResultSets=true");
        }

        public List<Blog> Get() =>
            _easyAccess.Query<Blog>("Blogs", Mapper.MapperOf<Blog>);

        public List<Blog> GetWithApplicationUserID(int applicationUserID) =>
            _easyAccess.Query<Blog>("Blogs",
                ConditionBuilder.ColumnEquals<Blog>(
                    nameof(Blog.ApplicationUserID), applicationUserID),
                Mapper.MapperOf<Blog>);

        public Blog Get(int id) =>
            _easyAccess.QuerySingle<Blog>("Blogs", $"Id = {id}", Mapper.MapperOf<Blog>);

        public (int id, bool inserted) Insert(Blog blog) =>
            _easyAccess.Insert("Blogs", blog);

        public (int id, bool updated) Update(Blog blog) =>
            _easyAccess.Update("Blogs", blog);

        public bool Delete(int id) =>
            _easyAccess.Delete("Blogs", id);
    }
}