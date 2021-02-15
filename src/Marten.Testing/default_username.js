// #region sample_default_username
module.exports = function (doc) {
    doc.UserName = (doc.FirstName + '.' + doc.LastName).toLowerCase();

    return doc;
}
// #endregion sample_default_username
