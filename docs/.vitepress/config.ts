import { BUNDLED_LANGUAGES } from 'shiki'
// @ts-expect-error
import { withMermaid } from "vitepress-plugin-mermaid"

// Include `cs` as alias for csharp
BUNDLED_LANGUAGES
  .find(lang => lang.id === 'csharp')!.aliases!.push('cs');

export default withMermaid({
  base: '/',
  lang: 'en-US',
  title: 'Marten',
  description: '.NET Transactional Document DB and Event Store on PostgreSQL',
  head: [
    ['link', { rel: 'apple-touch-icon', type: 'image/png', size: "180x180", href: '/apple-touch-icon.png' }],
    ['link', { rel: 'icon', type: 'image/png', size: "32x32", href: '/favicon-32x32.png' }],
    ['link', { rel: 'icon', type: 'image/png', size: "16x16", href: '/favicon-16x16.png' }],
    ['link', { rel: 'manifest', manifest: '/manifest.json' }],
    ['meta', { property: 'og:title', content: 'Marten' }],
    ['meta', { property: 'og:type', content: 'website' }],
    ['meta', { property: 'og:description', content: '.NET Transactional Document DB and Event Store on PostgreSQL' }],
    ['meta', { property: 'og:image', content: 'https://martendb.io/social.png' }],
    ['meta', { property: 'og:url', content: 'https://martendb.io' }],
    ['meta', { property: 'twitter:card', content: 'summary_large_image' }],
    ['meta', { property: 'twitter:site', content: 'marten_lib' }],
    ['meta', { property: 'twitter:creator', content: 'marten_lib' }],
    ['meta', { property: 'twitter:image', content: 'https://martendb.io/social.png' }]
  ],

  lastUpdated: true,

  themeConfig: {
    logo: '/logo.png',

    nav: [
      {
        text: 'latest (v6.x)',
        items: [
          { text: 'v5.x', link: 'https://marten-docs-v5.netlify.app', target: "_blank" },
          { text: 'v4.x', link: 'https://marten-docs-v4.netlify.app', target: "_blank" },
          { text: 'v3.x', link: 'https://martendb.io/v3/index.html', target: "_blank" }
        ]
      },
      { text: 'Intro', link: '/introduction' },
      { text: 'Document DB', link: '/documents/', activeMatch: '/documents/' },
      { text: 'Event Store', link: '/events/', activeMatch: '/events/' },
      { text: 'Migration', link: '/migration-guide' },
      { text: 'Support Plans', link: 'https://www.jasperfx.net/support-plans/' },
      { text: 'Join Chat', link: 'https://discord.gg/WMxrvegf8H' },
    ],

    // algolia: {
    //   appId: '9S7KY0SIDO',
    //   apiKey: '5b95a0e704fcf10d97ae621741cd907d',
    //   indexName: 'marten_index'
    // },
    search: {
      provider: 'local'
    },

    editLink: {
      pattern: 'https://github.com/JasperFx/marten/edit/master/docs/:path',
      text: 'Suggest changes to this page'
    },

    socialLinks: [
      { icon: 'twitter', link: 'https://twitter.com/intent/follow?original_referer=https%3A%2F%2Fmartendb.io%2F&ref_src=twsrc%5Etfw&region=follow_link&screen_name=marten_lib&tw_p=followbutton' },
      { icon: 'github', link: 'https://github.com/JasperFx/marten' },
    ],

    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © Jeremy D. Miller, Babu Annamalai, Oskar Dudycz, Joona-Pekka Kokko and contributors.',
    },

    sidebar: {
      '/': [
        {
          text: 'Introduction',
          collapsed: false,
          items: [
            { text: 'What is Marten?', link: '/introduction' },
            { text: 'Getting Started', link: '/getting-started' }
          ]
        },
        {
          text: 'Configuration',
          collapsed: false,
          items: [
            { text: 'Bootstrap with HostBuilder', link: '/configuration/hostbuilder' },
            { text: 'Configuring Document Storage', link: '/configuration/storeoptions' },
            { text: 'Json Serialization', link: '/configuration/json' },
            { text: 'Resiliency Policies', link: '/configuration/retries' },
            { text: 'Pre-Building Generated Types', link: '/configuration/prebuilding' },
            { text: 'Command Line Tooling', link: '/configuration/cli' },
            { text: 'Development versus Production Usage', link: '/configuration/optimized_artifact_workflow' },
            { text: 'Multi-Tenancy with Database per Tenant', link: '/configuration/multitenancy' },
            { text: 'Environment Checks', link: '/configuration/environment-checks' },
            { text: 'Custom IoC Integration', link: '/configuration/ioc' },
          ]
        },
        {
          text: 'Document Database',
          collapsed: false,
          items: [
            { text: 'Marten as Document DB', link: '/documents/' },
            { text: 'Document Identity', link: '/documents/identity' },
            { text: 'Database Storage', link: '/documents/storage' },
            { text: 'Marten Metadata', link: '/documents/metadata' },
            { text: 'Opening Sessions', link: '/documents/sessions' },
            { text: 'Storing Documents', link: '/documents/storing' },
            { text: 'Deleting Documents', link: '/documents/deletes' },
            {
              text: 'Querying Documents', link: '/documents/querying/', collapsed: false, items: [
                { text: 'Loading Documents by Id', link: '/documents/querying/byid' },
                { text: 'Querying Documents with Linq', link: '/documents/querying/linq/' },
                { text: 'Supported Linq Operators', link: '/documents/querying/linq/operators' },
                { text: 'Querying within Child Collections', link: '/documents/querying/linq/child-collections' },
                { text: 'Querying including Related Documents', link: '/documents/querying/linq/include' },
                { text: 'Querying to IAsyncEnumerable', link: '/documents/querying/linq/async-enumerable' },
                { text: 'Extending Marten\'s Linq Support', link: '/documents/querying/linq/extending' },
                { text: 'Searching on String Fields', link: '/documents/querying/linq/strings' },
                { text: 'Projection Operators', link: '/documents/querying/linq/projections' },
                { text: 'Paging', link: '/documents/querying/linq/paging' },
                { text: 'Mixing Raw SQL with Linq', link: '/documents/querying/linq/sql' },
                { text: 'Searching with Boolean Flags', link: '/documents/querying/linq/booleans' },
                { text: 'Searching for NULL Values', link: '/documents/querying/linq/nulls' },
                { text: 'Querying with Postgres SQL', link: '/documents/querying/sql' },
                { text: 'Querying for Raw JSON', link: '/documents/querying/query-json' },
                { text: 'Compiled Queries', link: '/documents/querying/compiled-queries' },
                { text: 'Batched Queries', link: '/documents/querying/batched-queries' },]
            },

            {
              text: 'Indexing Documents', link: '/documents/indexing/', collapsed: false, items: [
                { text: 'Calculated Indexes', link: '/documents/indexing/computed-indexes' },
                { text: 'Duplicated Fields', link: '/documents/indexing/duplicated-fields' },
                { text: 'Unique Indexes', link: '/documents/indexing/unique' },
                { text: 'Foreign Keys', link: '/documents/indexing/foreign-keys' },
                { text: 'GIN or GiST Indexes', link: '/documents/indexing/gin-gist-indexes' },
                { text: 'Metadata Indexes', link: '/documents/indexing/metadata-indexes' },
                { text: 'Ignore Indexes', link: '/documents/indexing/ignore-indexes' },]
            },


            { text: 'Execute custom SQL in session', link: '/documents/execute-custom-sql' },
            { text: 'Document Type Hierarchies', link: '/documents/hierarchies' },
            { text: 'Multi-Tenanted Documents', link: '/documents/multi-tenancy' },
            { text: 'Initial Baseline Data', link: '/documents/initial-data' },
            { text: 'Optimistic Concurrency', link: '/documents/concurrency' },
            { text: 'Full Text Searching', link: '/documents/full-text' },
            { text: 'Noda Time Support', link: '/documents/noda-time' },
            { text: 'PLv8 Support', link: '/documents/plv8' },
            { text: 'AspNetCore Support', link: '/documents/aspnetcore' },
          ]
        },
        {
          text: 'Event Store',
          collapsed: false,
          items: [
            { text: 'Marten as Event Store', link: '/events/' },
            { text: 'Quick Start', link: '/events/quickstart' },
            { text: 'Storage', link: '/events/storage' },
            { text: 'Appending Events', link: '/events/appending' },
            { text: 'Querying Events', link: '/events/querying' },
            { text: 'Metadata', link: '/events/metadata' },
            { text: 'Archiving Streams', link: '/events/archiving' },
            {
              text: 'Projections Overview', link: '/events/projections/', collapsed: false, items: [
                {
                  text: 'Aggregate Projections', link: '/events/projections/aggregate-projections', items: [
                    { text: 'Live Aggregations', link: '/events/projections/live-aggregates' },
                    { text: 'Multi-Stream Projections', link: '/events/projections/multi-stream-projections' },
                    { text: 'Custom Aggregations', link: '/events/projections/custom-aggregates' },]
                },
                { text: 'Event Projections', link: '/events/projections/event-projections' },
                { text: 'Custom Projections', link: '/events/projections/custom' },
                { text: 'Inline Projections', link: '/events/projections/inline' },
                { text: 'Flat Table Projections', link: '/events/projections/flat' },
                { text: 'Asynchronous Projections', link: '/events/projections/async-daemon' },
                { text: 'Rebuilding Projections', link: '/events/projections/rebuilding' },
                { text: 'Projections and IoC Services', link: '/events/projections/ioc' },
                { text: 'Async Daemon HealthChecks', link: '/events/projections/healthchecks' },]
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
              text: 'Aggregates, events and repositories',
              link: '/scenarios/aggregates-events-repositories'
            },
            { text: 'Understanding Event Sourcing', link: '/events/learning' },
          ]
        },
        {
          text: 'Testing',
          collapsed: false,
          items: [
            { text: 'Integration Testing', link: '/testing/integration' },
          ]
        },
        {
          text: 'DevOps',
          collapsed: false,
          items: [
            { text: 'DevOps', link: '/devops/devops' },
          ]
        },
        {
          text: 'Diagnostics',
          collapsed: false,
          items: [
            { text: 'Diagnostics and Instrumentation', link: '/diagnostics' },
          ]
        },
        {
          text: 'Schema',
          collapsed: false,
          items: [
            { text: 'Database Management', link: '/schema/' },
            { text: 'How Documents are Stored', link: '/schema/storage' },
            { text: 'Schema Migrations', link: '/schema/migrations' },
            { text: 'Exporting Schema Definition', link: '/schema/exporting' },
            { text: 'Schema Feature Extensions', link: '/schema/extensions' },
            { text: 'Tearing Down Document Storage', link: '/schema/cleaning' },
          ]
        },
        {
          text: 'Troubleshoot',
          collapsed: false,
          items: [
            { text: 'FAQ & Troubleshooting', link: '/troubleshoot' }
          ]
        },
        {
          text: 'Scenarios',
          collapsed: false,
          items: [
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
              text: 'Using sequences for unique identifiers',
              link: '/scenarios/using-sequence-for-unique-id'
            },
            {
              text: 'Command Handler Workflow for Capturing Events',
              link: '/scenarios/command_handler_workflow'
            }
          ]
        },
        {
          text: 'Postgres for SQL Server users',
          collapsed: false,
          items: [
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
              link: '/postgres/casing'
            },
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
            // {
            //   text: 'Backup and restore',
            //   link: '/postgres/backup-restore/'
            // },
            // {
            //   text: 'Local',
            //   link: '/postgres/backup-restore/local'
            // },
            // {
            //   text: 'Remote',
            //   link: '/postgres/backup-restore/remote'
            // },
          ]
        }
      ]
    }
  }
})

