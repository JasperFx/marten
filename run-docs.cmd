.paket\paket.exe restore

copy packages\storyteller\tools\embed.js documentation\content /Y

packages\storyteller\tools\st.exe doc-run -v 1.0
