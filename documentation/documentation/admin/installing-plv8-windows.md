<!--Title:Installing plv8 on Windows-->
<!--Url:installing-plv8-windows-->

In order to use the JavaScript functions, you need to install plv8. The Windows install of PostgreSQL 9.5 / 9.6, and possibly future versions, do not come with plv8 installed.

If you attempt to install the extension for your database:

    CREATE EXTENSION plv8;

You may be greeted with the following:

    sql> create extension plv8
    [2016-12-06 22:53:22] [58P01] ERROR: could not open extension control file "C:/Program Files/PostgreSQL/9.5/share/extension/plv8.control": No such file or directory

This means that the plv8 extension doesn't exist for you to install.

## Download

You can download the binaries from:

http://www.postgresonline.com/journal/archives/360-PLV8-binaries-for-PostgreSQL-9.5-windows-both-32-bit-and-64-bit.html

Note: This can be used for both 9.5 and 9.6, at the time of writing this, there is no compiled extension specific for 9.6.

Download the version that corresponds to the version of PostgreSQL you installed (32 or 64 bit)

## Install

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

## Create Extension

Once you have moved all the files to the correct folder, you can now call the create extension again:

    CREATE EXTENSION plv8;

This time you should get the message like:

    sql> create extension plv8
    [2016-12-06 23:12:10] completed in 2s 271ms

## Testing out the extension

To test out the extension you can create a super basic function which manipulates a string input and returns the value.

    create or replace function test_js_func(value text) returns text as $$

    var thing = 'I\' a JavaScript string';

    var result = thing.replace(/JavaScript/g, value);

    return result;

    $$ language plv8;

Then we can invoke it:

    select test_js_func('Manipulated');

And we should get a result that reads:

> I' a Manipulated string
