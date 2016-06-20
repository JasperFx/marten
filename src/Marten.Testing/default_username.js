// SAMPLE: default_username
module.exports = function (doc) {
    doc.UserName = (doc.FirstName + '.' + doc.LastName).toLowerCase();

    return doc;
}
// ENDSAMPLE