using MigrateContentToHeartcore.DTOs;
using System;
using System.Net.Http;
using System.Net.Http.Formatting;
using Umbraco.Headless.Client.Net.Management;
using Umbraco.Headless.Client.Net.Management.Models;
using System.Linq;
using Umbraco.Headless.Client.Net.Delivery;

int page = 0;

HttpClient client = new();

Uri ShowsAPI(int page) => new($"https://api.tvmaze.com/shows?page={page}");

//Project Alias
var projectAlias = "tvmaze-tvshowcatalog";
//User Api key
var apiKey = "GGPc6V0atAUQtV4qhG71";
//Creating an instance of the ContentManagementService class using projectAlias & apikey 
var ContentManagementService = new ContentManagementService(projectAlias, apiKey);
//Creating an instance of the ContentDeliveryService class using projectAlias
var ContentDeliveryService = new ContentDeliveryService(projectAlias);

var root = await ContentManagementService.Content.GetRoot();

var response = await client.GetAsync(ShowsAPI(page++));
if (!response.IsSuccessStatusCode)
{
    var errorContent = await response.Content.ReadAsStringAsync();
    Console.WriteLine($"Error: {response.StatusCode}, Details: {errorContent}");
}

// Deserialize response into TVMazeShow array
var apiShows = await response.Content.ReadAsAsync<TVMazeShow[]>(new[] { new JsonMediaTypeFormatter() });

//Writing line to show that the console is going to fetching the Tv Shows from the Api and creating them in the Heartcore Project.
Console.WriteLine("Fetching TV shows and creating content...");
List<Umbraco.Headless.Client.Net.Delivery.Models.Content> tvShows = new();
Guid parentGuid = Guid.Empty;


    foreach (var content in root)
    {
        Console.WriteLine($"Parent: {content.Name.FirstOrDefault().Value}");

        parentGuid = content.Id;
        if (content.HasChildren)
        {
            var tvShow = await ContentDeliveryService.Content.GetChildren(content.Id);

            // Null checks
            if (tvShow?.Content?.Items != null)
            {
                tvShows = (List<Umbraco.Headless.Client.Net.Delivery.Models.Content>)tvShow.Content.Items;
            }

        }


    // iterate through existing TV shows, extract ids and then remove these ids from ApiShows
    List<long> itemsToRemove = new();
    foreach (Umbraco.Headless.Client.Net.Delivery.Models.Content tvShow in tvShows)
    {
        object? tvShowId;
        tvShow.Properties.TryGetValue("showId", out tvShowId);
        if (tvShowId != null)
        {
            long toCompare = (long)tvShowId;
           itemsToRemove.Add(toCompare);
        }

    }
    List<TVMazeShow> apiShowsList = apiShows.ToList();

    foreach(long i in itemsToRemove) { 
        TVMazeShow targetShow = apiShows.Where(x => x.Id == i).FirstOrDefault();
        Console.WriteLine($"Tv shows that exists{targetShow.Name}");
        apiShowsList.Remove(targetShow);

    };
    //the ApiShowsList should now be clear of items we have previously made
    foreach (var apiShow in apiShowsList)
        {
           
        // Create content item for the show
        var newShow = new Content
        {
            ContentTypeAlias = "tvShow",
            ParentId = parentGuid
        };

        // Set the Name and other properties using SetValue method
        newShow.SetValue("name", apiShow.Name); // Set the Name
        newShow.SetValue("showSummary", apiShow.Summary ?? "Summary not available"); // Set the showSummary
        newShow.SetValue("showId", apiShow.Id);
        //newShow.SetValue("showImage,", show.Image)
        try
        {
            // Save & Publish the content item
            var createdShow = await ContentManagementService.Content.Create(newShow);
            var publishedShow = await ContentManagementService.Content.Publish(createdShow.Id);
            //Thread.Sleep(1000);
            //print out the show that has been crearted
            Console.WriteLine($"Created new show: {apiShow.Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating show '{apiShow.Name}': {ex.Message}");
        }
    }
    }
