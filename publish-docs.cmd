copy packages\storyteller\tools\embed.js documentation\content /Y

packages\storyteller\tools\ST.exe doc-export c:\code\marten-docs ProjectWebsite --version 1.0 --project marten

cd \code\marten-docs


git add --all



git commit -a -m "Documentation Update"



git push origin gh-pages
