// #region sample_get_fullname
module.exports = function (doc) {
    return {fullname: doc.FirstName + ' ' + doc.LastName};
}
// #endregion sample_get_fullname
