using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using IMDbApiLib;
using IMDbApiLib.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
//using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;
//using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
//using unirest_net.http;
//using MailKit.Net.Smtp;
//using MimeKit;

namespace dProject.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TrailerController : ControllerBase
    {
        protected readonly IOptions<ConfigData> _configData;

        private readonly ILogger<TrailerController> _logger;

        private readonly IConfiguration _secretConfigData;

        public object Configuration { get; private set; }

        public TrailerController(ILogger<TrailerController> logger, IOptions<ConfigData> configData, IConfiguration secretConfigData)
        {
            _logger = logger;
            _configData = configData;
            _secretConfigData = secretConfigData;
        }

        [HttpGet("Trailers")]
        public async Task<TrailerResult[]> GetTrailers(string searchText)
        {
            if (searchText == null || searchText == "")
            {
                throw new Exception("You must provide search text");
            }

            List<TrailerResult> trailers = new List<TrailerResult>();

            // get IMDB api library by API key
            var apiLib = new ApiLib(_secretConfigData["Secrets:ImdbAPIKey"]);

            // get results from IMDB endpoint "SearchTitle"
            var searchTitleResults = await apiLib.SearchTitleAsync(searchText);

            List<string> ids = new List<string>();

            if (searchTitleResults.Results != null)
            {
                // save id for every result
                foreach (var x in searchTitleResults.Results)
                {
                    ids.Add(x.Id);
                }

                // get trailer from IMDB endpoint "Trailer" for every result
                foreach (var id in ids)
                {
                    var trailerData = await apiLib.TrailerAsync(id);

                    // check is trailer found
                    if (trailerData != null && trailerData.Link != null)
                    {
                        TrailerResult trailer = new TrailerResult
                        {
                            Url = trailerData.Link,
                            Title = trailerData.FullTitle + " Trailer"
                        };

                        trailers.Add(trailer);
                    }
                }
            }

            // Get trailers from YouTube, using YouTubeAPI

            using (var youtubeService = new YouTubeService(new BaseClientService.Initializer()
            {
                ApiKey = _secretConfigData["Secrets:YouTubeAPIKey"],
            }))
            {
                var searchRequest = youtubeService.Search.List("snippet");
                searchRequest.MaxResults = 10;
                searchRequest.Q = searchText + "Trailer";

                var searchResponse = await searchRequest.ExecuteAsync();

                if (searchResponse.Items.Count != 0)
                {
                    // For every found video by searchText that user entered, get videoId to create video url
                    foreach (var searchResult in searchResponse.Items)
                    {
                        string url = _configData.Value.YouTubeURL;
                        var urlBuilder = new UriBuilder(url);
                        var query = HttpUtility.ParseQueryString(urlBuilder.Query);
                        query["v"] = searchResult.Id.VideoId;
                        urlBuilder.Query = query.ToString();
                        url = urlBuilder.ToString();

                        // check if the title contains "trailer" and searchText
                        if (searchResult.Snippet.Title.Contains("trailer", StringComparison.OrdinalIgnoreCase) && searchResult.Snippet.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                        {
                            TrailerResult trailer = new TrailerResult
                            {
                                Url = url,
                                Title = searchResult.Snippet.Title
                            };

                            trailers.Add(trailer);
                        }                       
                    }
                }               
            }

            if (trailers.Count == 0)
            {
                throw new Exception($"Trailer for {searchText} is not found.");
            }

            return trailers.ToArray();
        }

        [HttpPost("SendTrailer")]
        public async Task<SendTrailerResult> SendTrailer(string searchText, string emailAddress)
        {

            if (searchText == null || searchText == "" || emailAddress == null || emailAddress == "" )
            {
                return new SendTrailerResult
                {
                    Success = false,
                    Message = "You must provide search text and your email address."
                };
            }

            // check email format
            bool isEmailValid = Regex.IsMatch(emailAddress, @"^([\w-\.]+)@((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.)|(([\w-]+\.)+))([a-zA-Z]{2,4}|[0-9]{1,3})(\]?)$");

            if (!isEmailValid)
            {
                return new SendTrailerResult
                {
                    Success = false,
                    Message = "You must provide a valid email address."
                };
            }

            TrailerResult result = null;
            TrailerData trailer = null;

            var apiLib = new ApiLib(_secretConfigData["Secrets:ImdbAPIKey"]);

            var data = await apiLib.SearchTitleAsync(searchText);

            if (data != null && data.Results != null && data.Results.Count != null)
            {
                string id = data.Results.FirstOrDefault().Id;
                trailer = await apiLib.TrailerAsync(id);

                if (trailer.Link != null)
                {
                    result = new TrailerResult
                    {
                        Url = trailer.Link,
                        Title = trailer.FullTitle + " trailer"
                    };
                }
            }

            var password = _secretConfigData["Secrets:Password"];

            var smtpClient = new SmtpClient("smtp.gmail.com")
            {
                Port = 587,
                Credentials = new NetworkCredential(_configData.Value.EmailAddressOfSender, password),
                EnableSsl = true,
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(_configData.Value.EmailAddressOfSender),
                Subject = searchText + " Trailer",
                IsBodyHtml = true,
                Body = (data != null && data.Results != null && trailer != null && trailer.Link != null)
                    ?
                    "Hello,<br><br><b>TRAILER for "+ searchText + "</b><br><br> Here is your trailer " + result.Url + "<br><br>Yours sincerely<br>Andrea Kristo"
                    :
                    "Hello,</b><br><br> Unfortunately, the trailer for <b> " + searchText + "</b> was not found. <br><br>Yours sincerely<br>Andrea Kristo"
            };

            mailMessage.To.Add(emailAddress);

            try
            {
                smtpClient.Send(mailMessage);

                return new SendTrailerResult
                {
                    Success = true,
                    Message = "Message sent."
                };

            }
            catch
            {
                return new SendTrailerResult
                {
                    Success = false,
                    Message = "Some error occurred please try again later."
                };
            }           
        }
    }
}
