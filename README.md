# Notion to Markdown

The GitHub Actions for exporting Notion Database to local markdown files.

## Usage

### Example Workflow file

An example workflow to import markdown files to the repository.

```yml
name: import

on:
  schedule:
    - cron: '0/10 * * * *'
  workflow_dispatch:

jobs:
  import_markdown:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v2
      - uses: yucchiy/notion-to-markdown@main
        with:
          notion_auth_token: ${{ secrets.NOTION_AUTH_TOKEN }}
          notion_database_id: ${{ secrets.NOTION_DATABASE_ID }}
      - name: Push imported markdown files 
        run: |
          git config --local user.email "41898282+github-actions[bot]@users.noreply.github.com"
          git config --local user.name "github-actions[bot]"
          git add ./src/pages
          git commit -m "Import files from notion database"
      - uses: ad-m/github-push-action@master
        with:
          github_token: ${{ secrets.GITHUB_TOKEN }}
          branch: ${{ github.ref }}
```

### Inputs

| Name                             | Value  | Default                                               | Description                                        |
| -------------------------------- | ------ | ----------------------------------------------------- | -------------------------------------------------- |
| `notion_database_id`             | string | (required)                                            | Target Notion Database Id.                         | 
| `notion_auth_token`              | string | (required)                                            | Notion Token for accessing to your notion.         |
| `output_directory_path_template` | string | `output/{{publish|date.to_string('%Y/%m')}}/{{slug}}` | Directory path template for export markdown files. |

`output_directory_path_template` is used as a scriban template string. For detail of this template string, see [this page](https://github.com/scriban/scriban/blob/master/doc/language.md).

The following variables are passed to this template.

| Name      | C# Type  | Notion Property Name |
| --------- | -------- | -------------------- |
| `publish` | DateTime | `PublishedAt`        | 
| `title`   | string   | `Title`              |
| `slug`    | string   | `Slug`               |


### Outputs

| Name                |  Description                       |
| ------------------- | ---------------------------------- |
| `exported_count`    | Number of exported files actually. |  
