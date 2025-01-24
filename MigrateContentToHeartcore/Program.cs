using MigrateContentToHeartcore.DTOs;
using System;
using System.Net.Http;
using System.Net.Http.Formatting;
using Umbraco.Headless.Client.Net.Management;
using Umbraco.Headless.Client.Net.Management.Models;
using System.Linq;
using Umbraco.Headless.Client.Net.Delivery;
using Refit;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Reflection;

// Variable to track pagination of TVMaze API
int page = 0;

// Initialize HTTP client
HttpClient client = new();

// Function to generate the TVMaze API URL with pagination
Uri ShowsAPI(int page) => new($"https://api.tvmaze.com/shows?page={page}");

// Project alias for the Umbraco Heartcore instance
var projectAlias = "tvmaze-tvshowcatalog";

// API key for accessing Umbraco Heartcore
var apiKey = "zGEoec98bb5fPk3dxJPk";

// Create an instance of the ContentManagementService class using projectAlias and apiKey
var ContentManagementService = new ContentManagementService(projectAlias, apiKey);

// Create an instance of the ContentDeliveryService class using projectAlias
var ContentDeliveryService = new ContentDeliveryService(projectAlias);

// Get the root of the content tree
var root = await ContentManagementService.Content.GetRoot();
// Get the root of the media tree
var mediaRoot = await ContentManagementService.Media.GetRoot();

// HTTP GET request to the TVMaze API to fetch TV shows
var response = await client.GetAsync(ShowsAPI(page++));
if (!response.IsSuccessStatusCode)
{
    // Log error details if the request fails
    var errorContent = await response.Content.ReadAsStringAsync();
    Console.WriteLine($"Error: {response.StatusCode}, Details: {errorContent}");
}

// Deserialize the API response into an array of TVMazeShow objects
var apiShows = await response.Content.ReadAsAsync<TVMazeShow[]>(new[] { new JsonMediaTypeFormatter() });

// Inform user about the process of fetching TV shows and creating content
Console.WriteLine("Fetching TV shows and creating content...");

// List to store existing TV shows fetched from the Umbraco ContentDeliveryService
List<Umbraco.Headless.Client.Net.Delivery.Models.Content> tvShows = new();
Guid parentGuid = Guid.Empty; // Parent ID for creating new content
int pageSize = 100; // Number of items to fetch per page

foreach (var content in root)
{
    // Log the parent content name
    Console.WriteLine($"Parent: {content.Name.FirstOrDefault().Value}");

    parentGuid = content.Id; // Set parent GUID

    if (content.HasChildren)
    {
        // Initialize variable to store paged results
        Umbraco.Headless.Client.Net.Delivery.Models.PagedContent results = new();

        // Fetch child content in pages
        for (int i = 1; i < 10000; i++)
        {
            results = await ContentDeliveryService.Content.GetChildren(content.Id, null, i, pageSize);

            // Add retrieved items to the list of TV shows
            foreach (var item in results.Content.Items)
            {
                tvShows.Add(item);
            }

            // Check if more pages exist; if not, break the loop
            if (results.Content.Items.Count() != pageSize)
            {
                Console.WriteLine($"We have {tvShows.Count} shows, i was {i}");
                break;
            }
        }
    }

    // Identify and remove TV shows that already exist in Umbraco from the API data
    List<long> itemsToRemove = new();
    foreach (Umbraco.Headless.Client.Net.Delivery.Models.Content tvShow in tvShows)
    {
        // Try to get the "showId" property from the existing TV show
        object? tvShowId;
        tvShow.Properties.TryGetValue("showId", out tvShowId);
        if (tvShowId != null)
        {
            long toCompare = (long)tvShowId;
            itemsToRemove.Add(toCompare);
        }
    }

    // Convert API shows to a list and remove existing items
    List<TVMazeShow> apiShowsList = apiShows.ToList();
    foreach (long i in itemsToRemove)
    {
        TVMazeShow? targetShow = apiShows.Where(x => x.Id == i).FirstOrDefault();
        Console.WriteLine($"Tv shows that exist: {targetShow?.Name}");
        apiShowsList.Remove(targetShow);
    }
    var folder = mediaRoot?.FirstOrDefault();
    Guid folderId;
    if (folder == null)
    {
        var createdFolder = await ContentManagementService.Media.Create(new Media { MediaTypeAlias = "Folder", Name = "TV Show Media" });
        folderId = createdFolder.Id;
    }

    else
    {
        folderId = folder.Id;
    }
    // Create new TV shows in Umbraco for items not already present
    foreach (var apiShow in apiShowsList)
    {

        var imageResponse = await client.GetAsync(apiShow.Image.Medium);
        if (!imageResponse.IsSuccessStatusCode)
        {
            Console.WriteLine($"Failed to download image for {apiShow.Name}");
            continue;
        }
        var imageData = await imageResponse.Content.ReadAsByteArrayAsync();

        // Step 2: Determine the correct file extension (e.g., .jpg or .png)
        var imageUrl = apiShow.Image.Medium;
        var fileExtension = Path.GetExtension(imageUrl)?.TrimStart('.') ?? "jpg"; // Default to .jpg if no extension

        // Generate a proper file name
        var fileName = $"{apiShow.Name.Replace(" ", "_")}.{fileExtension}";

        // Save the file with the correct extension
        var tempImagePath = Path.Combine(Path.GetTempPath(), fileName);
        await File.WriteAllBytesAsync(tempImagePath, imageData);

        // Get file information
        var fileInfo = new FileInfo(tempImagePath);


        // Initialize new media item
        var newMedia = new Media
        {
            Name = $"{apiShow.Name} Image", // Media name
            ParentId = folderId, // Set parent ID (media root)
            MediaTypeAlias = "Image" // Use the "Image" cropper media type alias
        };
        newMedia.SetValue(
            "umbracoFile",
            new { src = fileName }, // Source filename for the image
            new FileInfoPart(fileInfo, fileName, $"image/{fileExtension}") // File info
        );

            var createdMedia = await ContentManagementService.Media.Create(newMedia);

        var dictionary = new List<Dictionary<string, string>>();
        var tvShowMediaRefrence = new Dictionary<string, string>() { 

         //Create new GUID for "key"
         { "key", Guid.NewGuid().ToString() },
         
         //Reference our media in "mediaKey"
         { "mediaKey", createdMedia.Id.ToString()},
         { "crops", null },
         { "focalPoint", null }};
        dictionary.Add(tvShowMediaRefrence);

        var tvShowMediaRefrenceJson = JsonConvert.SerializeObject(dictionary);
        var tvShowGenreNumber = apiShow.Genres.Length;
        var tvShowGenreType = apiShow.Genres;
        //blocklist item

        // Initialize new content item
        var newShow = new Content
        {
            ContentTypeAlias = "tvShow",
            ParentId = parentGuid
        };

        // Set properties for the new content item
        newShow.SetValue("name", apiShow.Name); // Set name
        newShow.SetValue("showGenre" , apiShow.Genres);
        newShow.SetValue("showSummary", apiShow.Summary ?? "Summary not available"); // Set summary
        newShow.SetValue("showId", apiShow.Id); // Set ID
        newShow.SetValue("showImage", tvShowMediaRefrenceJson);
        try
        {
            // Save and publish the content item in Umbraco
            var createdShow = await ContentManagementService.Content.Create(newShow);
            var publishedShow = await ContentManagementService.Content.Publish(createdShow.Id);
            await Task.Delay(1000); // Delay to avoid overwhelming the API

            // Log the successful creation of the show
            Console.WriteLine($"Created new show: {apiShow.Name}");
        }
        catch (Exception ex)
        {
            // Log any errors encountered during creation
            Console.WriteLine($"Error creating show '{apiShow.Name}': {ex.Message}");
        }
    }
}