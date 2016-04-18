packages\storyteller\tools\ST.exe doc-export c:\code\marten-docs ProjectWebsite --version 0.9.0 --project marten

cd \code\marten-docs


git add --all



git commit -a -m "Documentation Update"



git push origin gh-pages
