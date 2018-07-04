﻿using Core.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.SyndicationFeed;
using Microsoft.SyndicationFeed.Rss;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Core.Services
{
    public interface IFeedImportService
    {
        Task<List<ImportMessage>> Import(IFormFile file, string user);
        Task<List<ImportMessage>> Import(string fileName, string user);
    }

    public class FeedImportService : IFeedImportService
    {
        IUnitOfWork _db;
        IStorageService _ss;
        List<ImportMessage> _msgs;
        string _usr;
        string _url;

        public FeedImportService(IUnitOfWork db, IStorageService ss)
        {
            _db = db;
            _ss = ss;
            _msgs = new List<ImportMessage>();
        }

        public async Task<List<ImportMessage>> Import(IFormFile file, string usr)
        {
            _usr = usr;
            var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
            await ImportStream(reader);
            return await Task.FromResult(_msgs);
        }

        public async Task<List<ImportMessage>> Import(string fileName, string usr)
        {
            _usr = usr;
            var reader = new StreamReader(fileName, Encoding.UTF8);
            await ImportStream(reader);
            return await Task.FromResult(_msgs);
        }

        async Task ImportStream(StreamReader reader)
        {
            using (var xmlReader = XmlReader.Create(reader, new XmlReaderSettings() { }))
            {
                var feedReader = new RssFeedReader(xmlReader);

                while (await feedReader.Read())
                {
                    if (feedReader.ElementType == SyndicationElementType.Link)
                    {
                        var link = await feedReader.ReadLink();
                        _url = link.Uri.ToString();
                    }

                    if (feedReader.ElementType == SyndicationElementType.Item)
                    {
                        try
                        {
                            var item = await feedReader.ReadItem();

                            PostItem post = new PostItem
                            {
                                Author = await _db.Authors.GetItem(a => a.AppUserName == _usr),
                                Title = item.Title,
                                Description = item.Title,
                                Content = item.Description,
                                Slug = await GetSlug(item.Title),
                                Published = item.Published.DateTime,
                                Status = SaveStatus.Publishing
                            };

                            _msgs.Add(new ImportMessage { Status = Status.Success, Message = $"Post: {post.Title}" });

                            await ImportPost(post);
                        }
                        catch (Exception ex)
                        {
                            _msgs.Add(new ImportMessage { Status = Status.Error, Message = ex.Message });
                        }
                    }
                }
            }
        }

        async Task ImportPost(PostItem post)
        {
            var images = GetPostImages(post.Content);

            // await SaveAssets(images, post, false);

            var converter = new ReverseMarkdown.Converter();
            post.Content = converter.Convert(post.Content);

            // await _db.BlogPosts.SaveItem(post);
        }

        IList<string> GetPostImages(string html)
        {
            var links = new List<string>();
            string rgx = @"<img[^>]*?src\s*=\s*[""']?([^'"" >]+?)[ '""][^>]*?>";

            if (string.IsNullOrEmpty(html))
                return links;

            MatchCollection matchesImgSrc = Regex.Matches(html, rgx, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (matchesImgSrc != null)
            {
                foreach (Match m in matchesImgSrc)
                {
                    links.Add(m.Groups[1].Value);
                }
            }
            return links;
        }

        IList<string> GetAttachements(string html)
        {
            var links = new List<string>();
            string rgx = "<(a|link).*?href=(\"|')(.+?)(\"|').*?>";

            if (string.IsNullOrEmpty(html))
                return links;

            MatchCollection matches = Regex.Matches(html, rgx, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (matches != null)
            {
                foreach (Match m in matches)
                {
                    try
                    {
                        var link = m.Value.Replace("\">", "\"/>").ToLower();
                        var href = XElement.Parse(link).Attribute("href").Value;
                        links.Add(href);
                    }
                    catch { }
                }
            }
            return links;
        }

        async Task SaveAssets(IList<string> assets, PostItem post, bool isAttachement)
        {
            if (assets.Any())
            {
                foreach (var item in assets)
                {
                    var uri = "";
                    var webRoot = "/";
                    try
                    {
                        uri = ValidateUrl(item);

                        var path = string.Format("{0}/{1}", post.Published.Year, post.Published.Month);

                        AssetItem asset;
                        if (uri.Contains("data:image"))
                        {
                            asset = await _ss.UploadBase64Image(uri, webRoot, path);
                        }
                        else
                        {
                            asset = await _ss.UploadFromWeb(new Uri(uri), webRoot, path);
                        }

                        post.Content = post.Content.ReplaceIgnoreCase(uri.ToString(), asset.Url);

                        _db.Complete();
                    }
                    catch (Exception)
                    {
                        //_logger.LogError(string.Format("Error importing [{0}] : {1}", item, ex.Message));
                    }
                }
            }
        }

        async Task<string> GetSlug(string title)
        {
            string slug = title.ToSlug();
            BlogPost post;

            post = _db.BlogPosts.Single(p => p.Slug == slug);

            if (post == null)
                return await Task.FromResult(slug);

            for (int i = 2; i < 100; i++)
            {
                post = _db.BlogPosts.Single(p => p.Slug == $"{slug}{i}");

                if (post == null)
                {
                    return await Task.FromResult(slug + i.ToString());
                }
            }

            return await Task.FromResult(slug);
        }

        string ValidateUrl(string link)
        {
            var url = link;
            var domain = "";
            if (url.StartsWith("~"))
            {
                url = url.Replace("~", domain);
            }
            if (url.StartsWith("/"))
            {
                url = string.Concat(domain, url);
            }
            return url;
        }
    }

    public class ImportMessage
    {
        public Status Status { get; set; }
        public string Message { get; set; }
    }

    public enum Status
    {
        Success, Warning, Error
    }
}
