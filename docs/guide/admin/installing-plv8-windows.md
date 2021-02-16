# Installing plv8 on Windows

In order to use the JavaScript functions, you need to install plv8. The Windows install of PostgreSQL 9.5 / 9.6, and possibly future versions, do not come with plv8 installed.

If you attempt to install the extension for your database:

```sql
CREATE EXTENSION plv8;
```

You may be greeted with the following:

    sql> create extension plv8
    [2016-12-06 22:53:22] [58P01] ERROR: could not open extension control file "C:/Program Files/PostgreSQL/9.5/share/extension/plv8.control": No such file or directory

This means that the plv8 extension isn't installed so you're unable to create it in the database for usage.

## Download

You can download the appropriate binaries for PostgreSQL 9.5/PostgreSQL 9.6 from:

[PLV8-binaries-for-PostgreSQL-9.5-windows-both-32-bit-and-64-bit](http://www.postgresonline.com/journal/archives/360-PLV8-binaries-for-PostgreSQL-9.5-windows-both-32-bit-and-64-bit.html)

[PLV8-binaries-for-PostgreSQL-9.6beta1-windows-both-32-bit-and-64-bit](http://www.postgresonline.com/journal/archives/367-PLV8-binaries-for-PostgreSQL-9.6beta1-windows-both-32-bit-and-64-bit.html)

[PLV8-binaries-for-PostgreSQL-10-windows-both-32-bit-and-64-bit](http://www.postgresonline.com/journal/archives/379-PLV8-binaries-for-PostgreSQL-10-windows-both-32-bit-and-64-bit.htmll)

[xTuple-PLV8-binaries-for-PostgreSQL-9.4-to-12-windows-64-bit](http://updates.xtuple.com/updates/plv8/win/xtuple_plv8.zip)

Download the version that corresponds to the version of PostgreSQL you installed (32 or 64 bit)

## Install

### Distributions from Postgres Online

The zip should contain 3 folders:

- bin
- lib
- share

Move the contents of bin to:

> C:\Program Files\PostgreSQL\9.5\bin

Move the contents of lib to:

> C:\Program Files\PostgreSQL\9.5\lib

Move the contents of share/extension to:

> C:\Program Files\PostgreSQL\9.5\share\extension

The `Program Files` and `9.5` folders should correspond to the bit and version of PostgreSQL you installed. For example if you installed the 32 bit version of 9.6 then your path would be:

> C:\Program Files (x86)\PostgreSQL\9.6\

### Distributions from xTuple

The zip contains the folders for all the supported versions and the install_plv8.bat file.

Run the batch file from a command window running in administrative mode and provide the path for your Postgres installation.

## Create Extension

Once you have moved all the files to the correct folder, you can now call the create extension again:

```sql
CREATE EXTENSION plv8;
```

This time you should get the message like:

    sql> create extension plv8
    [2016-12-06 23:12:10] completed in 2s 271ms

If you get the below error while using the xTuple distribution

> ERROR:  syntax error in file "path_to_/plv8.control" line 1, near token ""
> SQL state: 42601

You need to ensure that the plv8.control is encoded with UTF-8. This is easiest to do with Notepad++.

## Testing out the extension

To test out the extension you can create a super basic function which manipulates a string input and returns the value.

```sql
create or replace function test_js_func(value text) returns text as $$

    var thing = 'I\' a JavaScript string';

    var result = thing.replace(/JavaScript/g, value);

    return result;

$$ language plv8;
```

Then we can invoke it:

```sql
select test_js_func('Manipulated');
```

And we should get a result that reads:

> I' a Manipulated string
