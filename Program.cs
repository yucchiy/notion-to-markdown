using Notion.Client;
using System.Net;
using System.Security.Cryptography;
using System.Text;

var notionTitlePropertyName = "Title";
var notionTypePropertyName = "Type";
var notionPublishedAtPropertyName = "PublishedAt";
var notionEditedAtPropertyName = "EditedAt";
var notionRequestPublisingPropertyName = "RequestPublishing";
var notionCrawledAtPropertyName = "_SystemCrawledAt";
var notionTagsPropertyName = "Tags";
var notionDescriptionPropertyName = "Description";
var notionSlugPropertyName = "Slug";

var frontMatterTitleName = "title";
var frontMatterTypeName = "type";
var frontMatterPublishedName = "date";
var frontMatterDescriptionName = "description";
var frontMatterTagsName = "tags";

// from CLI
var notionAuthToken = args[0];
var notionDatabaseId = args[1];
var baseOutputDirectory = args[2];

var pagination = await CreateNotionClient().Databases.QueryAsync(notionDatabaseId, new DatabasesQueryParameters()).ConfigureAwait(false);

var now = DateTime.Now;

do
{
    foreach (var page in pagination.Results)
    {
        if (await ExportPageToMarkdownAsync(baseOutputDirectory, page, now))
        {
            await CreateNotionClient().Pages.UpdateAsync(page.Id, new PagesUpdateParameters()
            {
                Properties = new Dictionary<string, PropertyValue>()
                {
                    [notionCrawledAtPropertyName] = new DatePropertyValue()
                    {
                        Date = new Date()
                        {
                            Start = now,
                        }
                    },
                    [notionRequestPublisingPropertyName] = new CheckboxPropertyValue()
                    {
                        Checkbox = false,
                    },
                }
            }).ConfigureAwait(false);
        }
    }

    if (!pagination.HasMore)
    {
        break;
    }

    pagination = await CreateNotionClient().Databases.QueryAsync(notionDatabaseId, new DatabasesQueryParameters
    {
        StartCursor = pagination.NextCursor,
    }).ConfigureAwait(false);
} while (true);

NotionClient CreateNotionClient()
{
    return NotionClientFactory.Create(new ClientOptions
    {
        AuthToken = notionAuthToken,
    });
}

async Task<bool> ExportPageToMarkdownAsync(string baseOutputDirectory, Page page, DateTime now, bool forceExport = false)
{
    bool requestPublishing = false;
    string title = string.Empty;
    string type = string.Empty;
    string slug = page.Id;
    string description = string.Empty;
    List<string>? tags = null;
    DateTime? publishedDateTime = null;
    DateTime? lastEditedDateTime = null;
    DateTime? lastSystemCrawledDateTime = null;

    // build frontmatter
    foreach (var property in page.Properties)
    {
        if (property.Key == notionPublishedAtPropertyName)
        {
            if (TryParsePropertyValueAsDateTime(property.Value, out var parsedPublishedAt))
            {
                publishedDateTime = parsedPublishedAt;
            }
        }
        else if (property.Key == notionCrawledAtPropertyName)
        {
            if (TryParsePropertyValueAsDateTime(property.Value, out var parsedCrawledAt))
            {
                lastSystemCrawledDateTime = parsedCrawledAt;
            }
        }
        else if (property.Key == notionEditedAtPropertyName)
        {
            if (TryParsePropertyValueAsDateTime(property.Value, out var parsedEditedAt))
            {
                lastEditedDateTime = parsedEditedAt;
            }
        }
        else if (property.Key == notionSlugPropertyName)
        {
            if (TryParsePropertyValueAsPlainText(property.Value, out var parsedSlug))
            {
                slug = parsedSlug;
            }
        }
        else if (property.Key == notionTitlePropertyName)
        {
            if (TryParsePropertyValueAsPlainText(property.Value, out var parsedTitle))
            {
                title = parsedTitle;
            }
        }
        else if (property.Key == notionDescriptionPropertyName)
        {
            if (TryParsePropertyValueAsPlainText(property.Value, out var parsedDescription))
            {
                description = parsedDescription;
            }
        }
        else if (property.Key == notionTagsPropertyName)
        {
            if (TryParsePropertyValueAsStringSet(property.Value, out var parsedTags))
            {
                tags = parsedTags.Select(tag => $"\"{tag}\"").ToList();
            }
        }
        else if (property.Key == notionTypePropertyName)
        {
            if (TryParsePropertyValueAsPlainText(property.Value, out var parsedType))
            {
                type = parsedType;
            }
        }
        else if (property.Key == notionRequestPublisingPropertyName)
        {
            if (TryParsePropertyValueAsBoolean(property.Value, out var parsedBoolean))
            {
                requestPublishing = parsedBoolean;
            }
        }
    }

    if (!requestPublishing && publishedDateTime.HasValue)
    {
        Console.WriteLine($"{page.Id}(title = {title}): No request publishing.");
        return false;
    }

    if (!publishedDateTime.HasValue || !lastEditedDateTime.HasValue)
    {
        Console.WriteLine($"{page.Id}(title = {title}): Don't have publish or last edited date.");
        return false;
    }

    if (!forceExport)
    {
        if (now < publishedDateTime.Value)
        {
            Console.WriteLine($"{page.Id}(title = {title}): Skip updating because of unreached published datetime.");
            return false;
        }

        if (lastSystemCrawledDateTime.HasValue)
        {
            if (lastEditedDateTime.Value <= lastSystemCrawledDateTime.Value)
            {
                Console.WriteLine($"{page.Id}(title = {title}): Skip updating because this article already updated.");
                return false;
            }
        }
    }

    slug = string.IsNullOrEmpty(slug) ? page.Id : slug;

    var stringBuilder = new StringBuilder();
    stringBuilder.AppendLine("---");

    if (!string.IsNullOrWhiteSpace(type)) stringBuilder.AppendLine($"{frontMatterTypeName}: \"{type}\"");
    stringBuilder.AppendLine($"{frontMatterTitleName}: \"{title}\"");
    if (!string.IsNullOrWhiteSpace(description)) stringBuilder.AppendLine($"{frontMatterDescriptionName}: \"{description}\"");
    if (tags != null) stringBuilder.AppendLine($"{frontMatterTagsName}: [{string.Join(',', tags)}]");
    stringBuilder.AppendLine($"{frontMatterPublishedName}: \"{publishedDateTime.Value.ToString("s")}\"");

    stringBuilder.AppendLine("");
    stringBuilder.AppendLine("---");
    stringBuilder.AppendLine("");

    var outputDirectory = BuildOutputDirectory(baseOutputDirectory, publishedDateTime.Value);
    if (!string.IsNullOrEmpty(slug))
    {
        outputDirectory = $"{outputDirectory}/{slug}";
    }

    if (!Directory.Exists(outputDirectory))
    {
        Directory.CreateDirectory(outputDirectory);
    }

    // page content
    var pagination = await CreateNotionClient().Blocks.RetrieveChildrenAsync(page.Id).ConfigureAwait(false);
    do
    {
        foreach (Block block in pagination.Results)
        {
            await AppendBlockLineAsync(block, string.Empty, outputDirectory, stringBuilder).ConfigureAwait(false);
        }

        if (!pagination.HasMore)
        {
            break;
        }

        pagination = await CreateNotionClient().Blocks.RetrieveChildrenAsync(page.Id, new BlocksRetrieveChildrenParameters
        {
            StartCursor = pagination.NextCursor,
        }).ConfigureAwait(false);
    } while (true);


    using (var fileStream = File.OpenWrite($"{outputDirectory}/index.markdown"))
    {
        using (var streamWriter = new StreamWriter(fileStream))
        {
            await streamWriter.WriteAsync(stringBuilder.ToString()).ConfigureAwait(false);
        }
    }

    return true;
}

string BuildOutputDirectory(string baseDirectoryPath, DateTime publishedDate)
{
    return $"{baseDirectoryPath}/{publishedDate.ToString("yyyy")}/{publishedDate.ToString("MM")}";
}

bool TryParsePropertyValueAsDateTime(PropertyValue value, out DateTime dateTime)
{
    dateTime = default;
    switch (value)
    {
        case DatePropertyValue dateProperty:
            if (dateProperty.Date == null) return false;
            if (!dateProperty.Date.Start.HasValue) return false;

            dateTime = dateProperty.Date.Start.Value;

            break;
        case CreatedTimePropertyValue createdTimeProperty:
            if (!DateTime.TryParse(createdTimeProperty.CreatedTime, out dateTime))
            {
                return false;
            }

            break;
        case LastEditedTimePropertyValue lastEditedTimeProperty:
            if (!DateTime.TryParse(lastEditedTimeProperty.LastEditedTime, out dateTime))
            {
                return false;
            }

            break;

        default:
            if (!TryParsePropertyValueAsPlainText(value, out var plainText))
            {
                return false;
            }

            if (!DateTime.TryParse(plainText, out dateTime))
            {
                return false;
            }

            break;
    }

    return true;
}

bool TryParsePropertyValueAsPlainText(PropertyValue value, out string text)
{
    text = string.Empty;
    switch (value)
    {
        case RichTextPropertyValue richTextProperty:
            foreach (var richText in richTextProperty.RichText)
            {
                text += richText.PlainText;
            }
            break;
        case TitlePropertyValue titleProperty:
            foreach (var richText in titleProperty.Title)
            {
                text += richText.PlainText;
            }
            break;
        case SelectPropertyValue selectPropertyValue:
            text = selectPropertyValue.Select.Name;
            break;

        default:
            return false;
    }

    return true;
}

bool TryParsePropertyValueAsStringSet(PropertyValue value, out List<string> set)
{
    set = new List<string>();
    switch (value)
    {
        case MultiSelectPropertyValue multiSelectProperty:
            foreach (var selectValue in multiSelectProperty.MultiSelect)
            {
                set.Add(selectValue.Name);
            }
            break;
        default:
            return false;
    }

    return true;
}

bool TryParsePropertyValueAsBoolean(PropertyValue value, out bool boolean)
{
    boolean = false;
    switch (value)
    {
        case CheckboxPropertyValue checkboxProperty:
            boolean = checkboxProperty.Checkbox;
            break;
        default:
            return false;
    }

    return true;
}

async Task AppendBlockLineAsync(Block block, string indent, string outputDirectory, StringBuilder stringBuilder)
{
    switch (block)
    {
        case ParagraphBlock paragraphBlock:
            foreach (var text in paragraphBlock.Paragraph.Text)
            {
                AppendRichText(text, stringBuilder);
            }

            stringBuilder.AppendLine(string.Empty);
            break;
        case HeadingOneBlock h1:
            stringBuilder.Append($"{indent}# ");
            foreach (var text in h1.Heading_1.Text)
            {
                AppendRichText(text, stringBuilder);
            }
            stringBuilder.AppendLine(string.Empty);
            break;
        case HeadingTwoBlock h2:
            stringBuilder.Append($"{indent}## ");
            foreach (var text in h2.Heading_2.Text)
            {
                AppendRichText(text, stringBuilder);
            }
            stringBuilder.AppendLine(string.Empty);
            break;
        case HeadingThreeeBlock h3:
            stringBuilder.Append($"{indent}### ");
            foreach (var text in h3.Heading_3.Text)
            {
                AppendRichText(text, stringBuilder);
            }
            stringBuilder.AppendLine(string.Empty);
            break;
        case ImageBlock imageBlock:
            await AppendImageAsync(imageBlock, indent, outputDirectory, stringBuilder).ConfigureAwait(false);
            stringBuilder.AppendLine(string.Empty);
            break;
        case CodeBlock codeBlock:
            AppendCode(codeBlock, indent, stringBuilder);
            stringBuilder.AppendLine(string.Empty);
            break;
        case BulletedListItemBlock bulletListItemBlock:
            AppendBulletListItem(bulletListItemBlock, indent, stringBuilder);
            break;
        case NumberedListItemBlock numberedListItemBlock:
            AppendNumberedListItem(numberedListItemBlock, indent, stringBuilder);
            break;
    }

    stringBuilder.AppendLine(string.Empty);

    if (block.HasChildren)
    {
        var pagination = await CreateNotionClient().Blocks.RetrieveChildrenAsync(block.Id).ConfigureAwait(false);
        do
        {
            foreach (Block childBlock in pagination.Results)
            {
                await AppendBlockLineAsync(childBlock, $"    {indent}", outputDirectory, stringBuilder).ConfigureAwait(false);
            }

            if (!pagination.HasMore)
            {
                break;
            }

            pagination = await CreateNotionClient().Blocks.RetrieveChildrenAsync(block.Id, new BlocksRetrieveChildrenParameters
            {
                StartCursor = pagination.NextCursor,
            }).ConfigureAwait(false);
        } while (true);
    }
}

void AppendTitleProperty(TitlePropertyValue titleProperty, StringBuilder stringBuilder)
{
    foreach (var richText in titleProperty.Title)
    {
        AppendRichText(richText, stringBuilder);
    }
}

void AppendRichText(RichTextBase richText, StringBuilder stringBuilder)
{
    var text = richText.PlainText;

    if (!string.IsNullOrEmpty(richText.Href))
    {
        text = $"[{text}]({richText.Href})";
    }

    if (richText.Annotations.IsCode)
    {
        text = $"`{text}`";
    }

    if (richText.Annotations.IsItalic && richText.Annotations.IsBold)
    {
        text = $"***{text}***";
    }
    else if (richText.Annotations.IsBold)
    {
        text = $"**{text}**";
    }
    else if (richText.Annotations.IsItalic)
    {
        text = $"*{text}*";
    }

    if (richText.Annotations.IsStrikeThrough)
    {
        text = $"~{text}~";
    }

    stringBuilder.Append(text);
}

async Task AppendImageAsync(ImageBlock imageBlock, string indent, string outputDirectory, StringBuilder stringBuilder)
{
    var url = string.Empty;
    switch (imageBlock.Image)
    {
        case ExternalFile externalFile:
            url = externalFile.External.Url;
            break;
        case UploadedFile uploadedFile:
            url = uploadedFile.File.Url;
            break;
    }

    if (!string.IsNullOrEmpty(url))
    {
        var uri = new Uri(url);
        using (var md5 = MD5.Create())
        {
            var input = Encoding.UTF8.GetBytes(uri.LocalPath);
            var fileName = $"{Convert.ToHexString(md5.ComputeHash(input))}{Path.GetExtension(uri.LocalPath)}";
            var filePath = $"{outputDirectory}/{fileName}";

            var client = new WebClient();
            await client.DownloadFileTaskAsync(uri, filePath).ConfigureAwait(false);

            stringBuilder.Append($"{indent}![](./{fileName})");
        }
    }
}

void AppendCode(CodeBlock codeBlock, string indent, StringBuilder stringBuilder)
{
    stringBuilder.AppendLine($"{indent}```{NotionCodeLanguageToMarkdownCodeLanguage(codeBlock.Code.Language)}");
    foreach (var richText in codeBlock.Code.Text)
    {
        stringBuilder.Append(indent);
        AppendRichText(richText, stringBuilder);
        stringBuilder.AppendLine(string.Empty);
    }
    stringBuilder.AppendLine($"{indent}```");
}

string NotionCodeLanguageToMarkdownCodeLanguage(string language)
{
    return language switch
    {
        "c#" => "csharp",
        _ => language,
    };
}

void AppendBulletListItem(BulletedListItemBlock bulletedListItemBlock, string indent, StringBuilder stringBuilder)
{
    stringBuilder.Append($"{indent}* ");
    foreach (var item in bulletedListItemBlock.BulletedListItem.Text)
    {
        AppendRichText(item, stringBuilder);
    }
}

void AppendNumberedListItem(NumberedListItemBlock numberedListItemBlock, string indent, StringBuilder stringBuilder)
{
    stringBuilder.Append($"{indent}1. ");
    foreach (var item in numberedListItemBlock.NumberedListItem.Text)
    {
        AppendRichText(item, stringBuilder);
    }
}

