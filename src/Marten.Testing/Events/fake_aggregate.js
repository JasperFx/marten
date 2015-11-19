var mt_transforms = require('mt_transforms');
mt_transforms.snapshot({
    name: 'fake_aggregate',
    $init: function() {
        return {ANames: [], BNames: [], CNames: [], DNames: []}
    },
    a_name: function(a, e) {
        a.ANames.push(e.Name);
    },
    b_name: function (a, e) {
        a.BNames.push(e.Name);
    },
    c_name: function (a, e) {
        c.ANames.push(e.Name);
    },
    d_name: function (a, e) {
        d.ANames.push(e.Name);
    }
})