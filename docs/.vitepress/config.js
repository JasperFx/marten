module.exports = {
  base: '/',
  lang: 'en-US',
  title: 'Marten',
  description: '.NET Transactional Document DB and Event Store on PostgreSQL',
  head: [
    ['link', { rel: 'apple-touch-icon', type: 'image/png', size: "180x180", href: '/apple-touch-icon.png' }],
    ['link', { rel: 'icon', type: 'image/png', size: "32x32", href: '/favicon-32x32.png' }],
    ['link', { rel: 'icon', type: 'image/png', size: "16x16", href: '/favicon-16x16.png' }],
    ['link', { rel: 'manifest', manifest: '/manifest.json' }]
  ],

  themeConfig: {
    logo: '/logo.png',
    repo: 'JasperFx/marten',
    docsDir: 'docs',
    docsBranch: 'master',
    editLinks: true,
    editLinkText: 'Suggest changes to this page',

    nav: [
      { text: 'v4.x',
        items: [
          { text: 'v3.x', link: '/v3/index.html', target:"_blank"}
        ]
      },
      { text: 'Intro', link: '/introduction' },
      { text: 'Config', link: '/configuration/' },
      { text: 'Document Database', link: '/documents/' },
      { text: 'Event Store', link: '/events/' },
      { text: 'Migration', link: '/migration-guide' },
      { text: 'Release Notes', link: 'https://github.com/JasperFx/marten/releases' },
      { text: 'Gitter | Join Chat', link: 'https://gitter.im/jasperfx/marten?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge' },
      { text: 'Twitter', link: 'https://twitter.com/intent/follow?original_referer=https%3A%2F%2Fmartendb.io%2F&ref_src=twsrc%5Etfw&region=follow_link&screen_name=marten_lib&tw_p=followbutton' },
    ],

    algolia: {
      appId: '9S7KY0SIDO',
      apiKey: '5b95a0e704fcf10d97ae621741cd907d',
      indexName: 'marten_index'
    },

    sidebar: [
      {
        text: 'Introduction',
        link: '/introduction'
      },
      {
        text:'Integration and Configuration',
        link: '/configuration/',
          children: [
              {text: 'Bootstrap with HostBuilder', link: '/configuration/hostbuilder'},
              {text: 'Do It Yourself IoC Integration', link: '/configuration/ioc'},
              {text: 'Command Line Tooling', link: '/configuration/cli'},
              {text: 'Configuring Document Storage with StoreOptions', link: '/configuration/storeoptions'},
              {text: 'Json Serialization', link: '/configuration/json'},
              {text: 'Retry Policies', link:'/configuration/retries'},
              {text: 'Pre-Building Generated Types', link: '/configuration/prebuilding'},
              {text: 'Multi-Tenancy with Database per Tenant', link: '/configuration/multitenancy'}
          ]
      },
      {
        text: 'Document Database',
        link: '/documents/',
        children: [
          {text: 'Identity', link: '/documents/identity'},
          {text: 'Storage', link: '/documents/storage'},
          {text: 'Metadata', link: '/documents/metadata'},
          {text: 'Sessions', link: '/documents/sessions'},
          {text: 'Storing', link: '/documents/storing'},
          {text: 'Deleting', link: '/documents/deletes'},
          {
            text: 'Querying',
            link: '/documents/querying/',
            children: [
                {text: 'Load Documents by Id', link: '/documents/querying/byid'},
                {text: 'Querying with Linq', link: '/documents/querying/linq/', children: [
                        {text: 'Supported Linq Operators', link: '/documents/querying/linq/operators'},
                        {text: 'Querying within Child Collections', link: '/documents/querying/linq/child-collections'},
                        {text: 'Including Related Documents', link: '/documents/querying/linq/include'},
                        {text: 'Querying to IAsyncEnumerable', link: '/documents/querying/linq/async-enumerable'},
                        {text: 'Extending Marten\'s Linq Support', link: '/documents/querying/linq/extending'},
                        {text: 'Searching on String Fields', link: '/documents/querying/linq/strings'},
                        {text: 'Projection Operators', link: '/documents/querying/linq/projections'},
                        {text: 'Paging', link: '/documents/querying/linq/paging'},
                        {text: 'Mixing Raw SQL with Linq', link: '/documents/querying/linq/sql'},
                        {text: 'Searching with Boolean Flags', link: '/documents/querying/linq/booleans'},
                        {text: 'Searching for NULL Values', link: '/documents/querying/linq/nulls'},
                    ]},
                {text: 'Querying with Postgres SQL', link: '/documents/querying/sql'},
                {text: 'Querying for Raw JSON', link: '/documents/querying/query-json'},
                {text: 'Compiled Queries', link: '/documents/querying/compiled-queries'},
                {text: 'Batched Queries', link: '/documents/querying/batched-queries'}


            ]
          },
          {
            text: 'Indexing',
            link: '/documents/indexing/',
            children: [
                {text: 'Calculated Indexes', link: '/documents/indexing/computed-indexes'},
                {text: 'Duplicated Fields', link: '/documents/indexing/duplicated-fields'},
                {text: 'Unique Indexes', link: '/documents/indexing/unique'},
                {text: 'Foreign Keys', link: '/documents/indexing/foreign-keys'},
                {text: 'GIN or GiST Indexes', link: '/documents/indexing/gin-gist-indexes'},
                {text: 'Metadata Indexes', link: '/documents/indexing/metadata-indexes'}
            ]
          },
          {text:'Document Type Hierarchies', link: '/documents/hierarchies'},
          {text:'Multi-Tenanted Documents', link: '/documents/multi-tenancy'},
          {text: 'Initial Baseline Data', link: '/documents/initial-data'},
          {text: 'Optimistic Concurrency', link: '/documents/concurrency'},
          {text: 'Full Text Searching', link: '/documents/full-text'},
          {text: 'Noda Time Support', link: '/documents/noda-time'},
          {text: 'PLv8 Support', link: '/documents/plv8'},
          {text: 'AspNetCore Support', link: '/documents/aspnetcore'}
        ]
      },
      {
        text: 'Event Store',
        link: '/events/',
        children: [
          {text: 'Quick Start', link: '/events/quickstart'},
          {text: 'Storage',link: '/events/storage'},
          {text: 'Appending Events', link: '/events/appending'},
          {text: 'Querying Events', link: '/events/querying'},
          {text: 'Metadata', link: '/events/metadata'},
          {text: 'Archiving Streams', link: '/events/archiving'},
          {
            text: 'Projections',
            link: '/events/projections/',
            children: [
              {text: 'Aggregate Projections', link: '/events/projections/aggregate-projections'},
              {text: 'Live Aggregations', link: '/events/projections/live-aggregates'},
              {text: 'View Projections', link: '/events/projections/view-projections'},
              {text: 'Event Projections', link: '/events/projections/event-projections'},
              {text: 'Custom Projections', link: '/events/projections/custom'},
              {text: 'Inline Projections', link: '/events/projections/inline'},
              {text: 'Asynchronous Projections',link: '/events/projections/async-daemon'},
              {text: 'Rebuilding Projections', link: '/events/projections/rebuilding'}
            ]
          },

          {
            text: 'Event Versioning',
            link: '/events/versioning'
          },



          {
            text: 'Multitenancy',
            link: '/events/multitenancy'
          },
          {
            text: 'Advanced',
            children: [
              {
                text: 'Aggregates, events and repositories',
                link: '/scenarios/aggregates-events-repositories'
              },
              {
                text: 'Copy and transform stream',
                link: '/scenarios/copy-and-transform-stream'
              },
              {
                text: 'Immutable projections as read model',
                link: '/scenarios/immutable-projections-read-model'
              },
            ]
          }
        ]
      },
      {text: 'Diagnostics and Instrumentation', link: '/diagnostics'},
      {
        text: 'Database Management',
        link: '/schema/',
        children: [
          {text: 'How Documents are Stored', link: '/schema/storage'},
          {text: 'Schema Migrations', link: '/schema/migrations'},
          {text: 'Exporting Schema Definition', link: '/schema/exporting'},
          {text: 'Schema Feature Extensions', link: '/schema/extensions'},
          {text: 'Tearing Down Document Storage', link: '/schema/cleaning'},
        ]
      },
      {text: 'FAQ & Troubleshooting', link: '/troubleshoot'},
      {
        text: 'Scenarios',
        link: '/scenarios/',
        children: [
          {
            text: 'Aggregates, events and repositories',
            link: '/scenarios/aggregates-events-repositories'
          },
          {
            text: 'Copy and transform stream',
            link: '/scenarios/copy-and-transform-stream'
          },
          {
            text: 'Dynamic data',
            link: '/scenarios/dynamic-data'
          },
          {
            text: 'Immutable projections as read model',
            link: '/scenarios/immutable-projections-read-model'
          },
          {
            text: 'Using sequences for unique identifiers',
            link: '/scenarios/using-sequence-for-unique-id'
          },
        ]
      },
      {
        text: 'Postgres for SQL Server users',
        link: '/postgres/',
        children: [
          {
            text: 'Naming conventions',
            link: '/postgres/naming'
          },
          {
            text: 'Escaping',
            link: '/postgres/escaping'
          },
          {
            text: 'Types',
            link: '/postgres/types'
          },
          {
            text: 'Casting',
            link: '/postgres/casting'
          },
          {
            text: 'Casing',
            link: '/postgres/casing/',
            children: [
              {
                text: 'Unique values',
                link: '/postgres/casing/unique-values'
              },
              {
                text: 'Case insensitive data',
                link: '/postgres/casing/case-insensitive-data'
              },
              {
                text: 'Queries',
                link: '/postgres/casing/queries'
              },
              {
                text: 'Using duplicate fields',
                link: '/postgres/casing/using-duplicate-fields'
              },
            ]
          },
          {
            text: 'Slow queries',
            link: '/postgres/slow-queries'
          },
          {
            text: 'Indexing',
            link: '/postgres/indexing'
          },
          {
            text: 'Indexing JSONB',
            link: '/postgres/indexing-jsonb'
          },
          {
            text: 'If statements',
            link: '/postgres/if-statements'
          },
          {
            text: 'Working with dates',
            link: '/postgres/dates'
          },
          {
            text: 'Backup and restore',
            link: '/postgres/backup-restore/',
            children: [
              {
                text: 'Local',
                link: '/postgres/backup-restore/local'
              },
              {
                text: 'Remote',
                link: '/postgres/backup-restore/remote'
              }
            ]
          }
        ]
      }
    ]
  }
}
