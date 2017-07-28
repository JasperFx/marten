module.exports = function (doc) {
    doc.userName = (doc.firstName + '.' + doc.lastName).toLowerCase();

    return doc;
}