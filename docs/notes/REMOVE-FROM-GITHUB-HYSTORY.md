To remove a file from Git history because the repository is growing too large:

If you publish files like this for years:

aifa-202401.zip
aifa-202402.zip
...
aifa-203012.zip

...simply deleting the files from the branch won't remove them from the history; they will still be there.

In that case, you can periodically:

create a new orphan `gh-pages` branch:
`git checkout --orphan gh-pages-new`

copy only the ZIP files you want to keep;
delete the old branch;
rename the new branch to `gh-pages`.

Essentially, you are resetting the branch's history.

For your AIFA case, I would do this:

keep only the last 12 ZIP files on `gh-pages`;
recreate the `gh-pages` branch from scratch once a year.

This keeps the size down without affecting the history of the main branch or interfering with your application updates.