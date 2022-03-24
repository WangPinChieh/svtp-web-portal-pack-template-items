using System.Data.SqlClient;
using System.IO.Compression;
using Amazon;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace svtp_web_portal_pack_template_items;

public class Function
{
    private const string BucketName = "svtp-webportal-dev";

    /// <summary>
    /// A simple function that takes a string and does a ToUpper
    /// </summary>
    /// <param name="input"></param>
    /// <param name="context"></param>
    /// <returns></returns>
    public async Task<string> FunctionHandler(Parameter parameter, ILambdaContext context)
    {
        var files = await GetS3Files(parameter);
        if (files.Length == 0)
        {
            throw new Exception($"Nothing found on S3. Parameters: {parameter}");
        }

        var zipFileStream = await Archive(files);
        var preSignedUrl = await UploadZipFile(parameter, zipFileStream);

        using (var sqlConnection =
               new SqlConnection( ""))
        {
           await sqlConnection.OpenAsync();
           try
           {
               using (var command =
                      new SqlCommand(
                          $"UPDATE tblZipFile SET Status = 1, Url = '{preSignedUrl}', UpdatedAt = GETDATE() WHERE Id='{parameter.ZipFileRetrievalKey}'",
                          sqlConnection))
               {
                   await command.ExecuteNonQueryAsync();
               }
           }
           catch (Exception exp)
           {
               throw new Exception("Update database failed.", exp);
           }
           finally
           {
               await sqlConnection.CloseAsync();
           }
        }


        return preSignedUrl;
    }

    private async Task<string> UploadZipFile(Parameter parameter, MemoryStream zipFileStream)
    {
        using (var s3Client = new AmazonS3Client(RegionEndpoint.USWest2))
        {
            var uploadZipFileKey =
                $"Download/{parameter.TemplateVersion}_{parameter.Project}_{parameter.Phase}_{DateTime.Now.ToString("yyyyMMddHmsffff")}.zip";
            var uploadResult = await s3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = BucketName,
                Key = uploadZipFileKey,
                InputStream = new MemoryStream(zipFileStream.ToArray())
            });
            var preSignedUrl = s3Client.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = BucketName,
                Key = uploadZipFileKey,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.AddMinutes(30)
            });
            return preSignedUrl;
        }
    }

    private async Task<MemoryStream> Archive(IEnumerable<GetObjectResponse> files)
    {
        await using var zipFileStream = new MemoryStream();
        using var zipArchive = new ZipArchive(zipFileStream, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            await AddToZip(zipArchive, file.Key.Replace("/", "_"), file.ResponseStream);
        }

        zipArchive.Dispose();
        return zipFileStream;
    }

    private async Task<GetObjectResponse[]> GetS3Files(Parameter parameter)
    {
        using (var s3Client = new AmazonS3Client(RegionEndpoint.USWest2))
        {
            var result = await s3Client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = BucketName,
                Prefix = $"Templates/{parameter.TemplateVersion}/",
            }, CancellationToken.None);

            var tasks = new List<Task<GetObjectResponse>>();
            foreach (var s3Object in result.S3Objects)
            {
                tasks.Add(s3Client.GetObjectAsync(BucketName, s3Object.Key, CancellationToken.None));
            }

            var files = await Task.WhenAll(tasks.ToArray());
            return files;
        }
    }

    private async Task AddToZip(ZipArchive zipArchive, string fileName, Stream fileStream)
    {
        var zipArchiveEntry = zipArchive.CreateEntry(fileName, CompressionLevel.Optimal);
        await using var zipEntryStream = zipArchiveEntry.Open();
        await fileStream.CopyToAsync(zipEntryStream);
    }
}
