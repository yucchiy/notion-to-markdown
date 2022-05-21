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
      - uses: actions/checkout@v2.4.2
      - uses: yucchiy/notion-to-markdown@main
        with:
          notion_auth_token: ${{ secrets.NOTION_AUTH_TOKEN }}
          notion_database_id: ${{ secrets.NOTION_DATABASE_ID }}
          output_directory_path : './src/pages'
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

| Name                    | Value  | Default    | Description                                |
| ----------------------- | ------ | ---------- | ------------------------------------------ |
| `notion_database_id`    | string | (required) | Target Notion Database Id.                 | 
| `notion_auth_token`     | string | (required) | Notion Token for accessing to your notion. |
| `output_directory_path` | string | `.`        | Directory for export markdown files.       |

### Outputs

| Name                |  Description                       |
| ------------------- | ---------------------------------- |
| `exported_count`    | Number of exported files actually. |  
