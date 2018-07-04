﻿using System;

namespace Core.Data
{
    public interface IUnitOfWork : IDisposable
    {
        IPostRepository BlogPosts { get; }
        IAuthorRepository Authors { get; }

        int Complete();
    }

    public class UnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;

        public UnitOfWork(AppDbContext db)
        {
            _db = db;

            BlogPosts = new PostRepository(_db);
            Authors = new AuthorRepository(_db);
        }

        public IPostRepository BlogPosts { get; private set; }
        public IAuthorRepository Authors { get; private set; }

        public int Complete()
        {
            return _db.SaveChanges();
        }

        public void Dispose()
        {
            _db.Dispose();
        }
    }
}